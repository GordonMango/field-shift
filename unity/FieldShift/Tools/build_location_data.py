# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "tifffile", "imagecodecs", "shapely"]
# ///
"""
Builds the real-world data each Field Shift location needs and writes it into
Assets/FieldShift/Resources/FieldShift/Locations/<id>/.

For every location it fetches:
  * NASA POWER daily agroclimate data (2019-2023) for the farm point      -> weather.json
  * NASADEM 1 arc-second elevation (NASA/JPL reprocessed SRTM), 8 x 8 km -> heightmap_raw.bytes + heightmap.json
  * First Nations reserve boundaries (Natural Resources Canada "Aboriginal Lands"
    as imported into OpenStreetMap from GeoBase) and water bodies (OpenStreetMap)
    rasterised to a 20 m grid                                             -> landmask.bytes + reserves.json

Run from unity/FieldShift:
    uv run Tools/build_location_data.py [cache_dir] [location ids...] [--mask-only]
Downloads use curl (system certificates). Nothing here needs an API key.
"""
import json, math, os, re, subprocess, sys
import numpy as np, tifffile
import shapely
from shapely.geometry import LineString, Polygon
from shapely.ops import polygonize, unary_union

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_ROOT = os.path.join(HERE, "..", "Assets", "FieldShift", "Resources", "FieldShift", "Locations")
CACHE = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, ".cache")
SIZE_M, HM_RES, MASK_RES = 8000.0, 1025, 400          # 8 km map, ~7.8 m heightmap, 20 m land mask
M_PER_DEG = 111320.0

LOCATIONS = [
    dict(id="sumas",    lat=49.065, lon=-122.175),   # Sumas Prairie, Fraser Valley: old lake bed beside Upper Sumas 6
    dict(id="oliver",   lat=49.180, lon=-119.550),   # Oliver, South Okanagan
    dict(id="cowichan", lat=48.765, lon=-123.665),   # Cowichan Valley near Duncan, Vancouver Island
    dict(id="peace",    lat=55.760, lon=-120.240),   # Dawson Creek, Peace River region
]
POWER_PARAMS = "T2M_MAX,T2M_MIN,T2M,PRECTOTCORR,ALLSKY_SFC_SW_DWN,RH2M,WS2M,GWETTOP,GWETROOT"


def curl(url, dest=None, data=None, timeout=300):
    cmd = ["curl", "-sSfL", "--retry", "2", "--max-time", str(timeout), "-A", "FieldShift-hackathon/0.1"]
    if data is not None:
        cmd += ["--data-urlencode", data]
    cmd.append(url)
    if dest:
        cmd += ["-o", dest]
        subprocess.run(cmd, check=True)
        return None
    return subprocess.run(cmd, check=True, capture_output=True).stdout


def bbox(loc):
    dlat = SIZE_M / M_PER_DEG
    dlon = SIZE_M / (M_PER_DEG * math.cos(math.radians(loc["lat"])))
    return loc["lat"] - dlat / 2, loc["lat"] + dlat / 2, loc["lon"] - dlon / 2, loc["lon"] + dlon / 2


def to_local(loc, lon, lat):
    """Equirectangular metres from the map centre: x east, z north."""
    return ((lon - loc["lon"]) * M_PER_DEG * math.cos(math.radians(loc["lat"])), (lat - loc["lat"]) * M_PER_DEG)


# ---------------------------------------------------------------- NASA POWER
def build_weather(loc, out):
    url = ("https://power.larc.nasa.gov/api/temporal/daily/point?parameters=" + POWER_PARAMS +
           f"&community=AG&longitude={loc['lon']}&latitude={loc['lat']}&start=20190101&end=20231231&format=JSON")
    d = json.loads(curl(url))
    p = d["properties"]["parameter"]
    dates = sorted(p["T2M"].keys())

    def series(key):
        v = np.array([p[key][k] for k in dates], dtype=float)
        bad = v < -900
        if bad.any():
            good = np.where(~bad)[0]
            v[bad] = np.interp(np.where(bad)[0], good, v[good])
        return [round(float(x), 2) for x in v]

    w = dict(source="NASA POWER daily, AG community (power.larc.nasa.gov). Sources: " + ", ".join(d["header"].get("sources", [])),
             lat=loc["lat"], lon=loc["lon"], powerCellElevation=d["geometry"]["coordinates"][2],
             startDate=dates[0], days=len(dates),
             tmax=series("T2M_MAX"), tmin=series("T2M_MIN"), tmean=series("T2M"), precip=series("PRECTOTCORR"),
             solar=series("ALLSKY_SFC_SW_DWN"), rh=series("RH2M"), wind=series("WS2M"),
             gwetTop=series("GWETTOP"), gwetRoot=series("GWETROOT"))
    json.dump(w, open(os.path.join(out, "weather.json"), "w"), separators=(",", ":"))
    return w


# ---------------------------------------------------------------- NASADEM
def nasadem_tile(lat, lon):
    return f"n{int(math.floor(lat)):02d}w{int(-math.floor(lon)):03d}"


def build_heightmap(loc, out):
    tile = nasadem_tile(loc["lat"], loc["lon"])
    path = os.path.join(CACHE, f"NASADEM_HGT_{tile}.tif")
    if not os.path.exists(path):
        tok = json.loads(curl("https://planetarycomputer.microsoft.com/api/sas/v1/token/nasademeuwest/nasadem-cog"))["token"]
        curl(f"https://nasademeuwest.blob.core.windows.net/nasadem-cog/v001/NASADEM_HGT_{tile}.tif?{tok}", dest=path)
    a = tifffile.imread(path).astype(np.float64)
    a[a < -100] = np.nan
    n = a.shape[0] - 1                                  # samples per degree
    top_lat, left_lon = math.floor(loc["lat"]) + 1, math.floor(loc["lon"])
    s, nn, w, e = bbox(loc)
    ys, xs = np.linspace(s, nn, HM_RES), np.linspace(w, e, HM_RES)
    R, C = np.meshgrid((top_lat - ys) * n, (xs - left_lon) * n, indexing="ij")
    r0, c0 = np.floor(R).astype(int), np.floor(C).astype(int)
    fr, fc = R - r0, C - c0
    g = lambda r, c: a[np.clip(r, 0, n), np.clip(c, 0, n)]
    h = g(r0, c0) * (1 - fr) * (1 - fc) + g(r0 + 1, c0) * fr * (1 - fc) + g(r0, c0 + 1) * (1 - fr) * fc + g(r0 + 1, c0 + 1) * fr * fc
    h = np.where(np.isnan(h), np.nanmin(h), h)
    hmin, hmax = float(h.min()), float(h.max())
    datum = math.floor(min(hmin, 0.0) if hmin < 50 else hmin - 20)   # keep real metres; low valleys keep a sea-level datum
    rng = math.ceil((hmax - datum) / 50.0) * 50.0
    ((np.clip((h - datum) / rng, 0, 1)) * 65535).round().astype("<u2").tofile(os.path.join(out, "heightmap_raw.bytes"))
    meta = dict(source=f"NASADEM_HGT v001 tile {tile} (NASA/JPL reprocessed SRTM, 1 arc-second), via Microsoft Planetary Computer",
                sizeMeters=SIZE_M, resolution=HM_RES, south=s, north=nn, west=w, east=e,
                datumMeters=datum, heightRangeMeters=rng, minElevation=hmin, maxElevation=hmax,
                layout="uint16 little-endian, row 0 = south edge, column 0 = west edge")
    json.dump(meta, open(os.path.join(out, "heightmap.json"), "w"), indent=1)
    return meta


# ---------------------------------------------------------------- reserves + water (OpenStreetMap / GeoBase)
def overpass(query):
    last = None
    for endpoint in ("https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
                     "https://overpass-api.de/api/interpreter"):
        try:
            return json.loads(curl(endpoint, data="data=" + query, timeout=180))
        except Exception as ex:                      # busy servers drop connections; try the next one
            last = ex
            print(f"   overpass retry after: {ex}")
    raise last


def element_polygon(loc, el):
    """Polygon (local metres) from an Overpass element fetched with `out geom`."""
    def line(geom):
        return LineString([to_local(loc, p["lon"], p["lat"]) for p in geom])
    if el["type"] == "way":
        g = el.get("geometry") or []
        if len(g) >= 4 and g[0] == g[-1]:
            return Polygon([to_local(loc, p["lon"], p["lat"]) for p in g])
        return None
    outers, inners = [], []
    for m in el.get("members", []):
        if m.get("type") == "way" and m.get("geometry"):
            (inners if m.get("role") == "inner" else outers).append(line(m["geometry"]))
    if not outers:
        return None
    poly = unary_union(list(polygonize(unary_union(outers))))
    if inners:
        poly = poly.difference(unary_union(list(polygonize(unary_union(inners)))))
    return poly if not poly.is_empty else None


def build_landmask(loc, out):
    s, n, w, e = bbox(loc)
    pad = 0.02
    bb = f"{s - pad},{w - pad},{n + pad},{e + pad}"
    # Only legal reserve parcels: the GeoBase import of NRCan Aboriginal Lands, or reserves named like "Osoyoos 1".
    # Nation / tribal-council / territory outlines are huge and are not land parcels, so they are left out.
    res = overpass(f'[out:json][timeout:120];(nwr["boundary"="aboriginal_lands"]["source"~"GeoBase"]({bb});'
                   f'nwr["boundary"="aboriginal_lands"]["attribution"~"GeoBase"]({bb});'
                   f'nwr["boundary"="aboriginal_lands"]["name"~" [0-9]+[A-Za-z]?$"]({bb});'
                   f'nwr["boundary"="aboriginal_lands"]["name"~"(Indian Reserve|IR [0-9])"]({bb}););out geom;')
    half = SIZE_M / 2
    cell = SIZE_M / MASK_RES
    xs = -half + cell * (np.arange(MASK_RES) + 0.5)
    X, Z = np.meshgrid(xs, xs)                          # row = z (south -> north), col = x (west -> east)
    reserve_id = np.zeros((MASK_RES, MASK_RES), dtype=np.uint8)
    reserves = []
    for el in res["elements"]:
        tags = el.get("tags", {})
        name = tags.get("name", "")
        official = ("GeoBase" in (tags.get("source", "") + tags.get("attribution", ""))
                    or bool(re.search(r" [0-9]+[A-Za-z]?$", name)) or "Indian Reserve" in name)
        poly = element_polygon(loc, el)
        if poly is None:
            continue
        area_km2 = poly.area / 1e6
        inside = shapely.contains_xy(poly, X, Z)
        print(f"   {tags.get('name')!r}: official reserve={official}, area={area_km2:.1f} km2, cells on map={int(inside.sum())}")
        if not official or not inside.any():
            continue                                    # nation-wide or territory outlines are not reserve parcels
        reserves.append(dict(id=len(reserves) + 1, name=tags.get("name", "Reserve"), osmId=el["id"],
                             wikidata=tags.get("wikidata", ""), areaKm2=round(area_km2, 2),
                             cells=int(inside.sum())))
        reserve_id[inside & (reserve_id == 0)] = len(reserves)
    # Water: closed water ways and river/canal centre lines. Big multipolygon lakes are skipped because their full
    # geometry is huge; the heightmap already shows them as flat low ground.
    water = np.zeros((MASK_RES, MASK_RES), dtype=bool)
    try:
        wat = overpass(f'[out:json][timeout:120];(way["natural"="water"]({bb});way["waterway"="riverbank"]({bb});'
                       f'way["waterway"~"^(river|canal)$"]({bb}););out geom;')
    except Exception as ex:
        print(f"   water lookup failed, continuing without water mask: {ex}")
        wat = {"elements": []}
    for el in wat["elements"]:
        tags = el.get("tags", {})
        if tags.get("waterway") in ("river", "canal") and el["type"] == "way":
            g = el.get("geometry") or []
            if len(g) >= 2:
                geom = LineString([to_local(loc, p["lon"], p["lat"]) for p in g]).buffer(18 if tags["waterway"] == "river" else 8)
            else:
                continue
        else:
            geom = element_polygon(loc, el)
        if geom is not None:
            water |= shapely.contains_xy(geom, X, Z)
    np.concatenate([reserve_id.ravel(), water.astype(np.uint8).ravel()]).tofile(os.path.join(out, "landmask.bytes"))
    info = dict(source="Reserve boundaries: Natural Resources Canada 'Aboriginal Lands of Canada Legislative Boundaries' "
                       "(GeoBase import in OpenStreetMap, ODbL). Water: OpenStreetMap contributors (ODbL).",
                grid=MASK_RES, cellMeters=cell,
                layout="uint8 reserve id grid (row 0 = south) followed by uint8 water grid, same size",
                reserves=reserves, waterCells=int(water.sum()))
    json.dump(info, open(os.path.join(out, "reserves.json"), "w"), indent=1)
    return info


def main():
    os.makedirs(CACHE, exist_ok=True)
    mask_only = "--mask-only" in sys.argv
    only = set(a for a in sys.argv[2:] if not a.startswith("--"))
    for loc in LOCATIONS:
        if only and loc["id"] not in only:
            continue
        out = os.path.join(OUT_ROOT, loc["id"])
        os.makedirs(out, exist_ok=True)
        print(f"== {loc['id']}")
        if not mask_only:
            w = build_weather(loc, out)
            print(f"   POWER: {w['days']} days from {w['startDate']}")
            m = build_heightmap(loc, out)
            print(f"   NASADEM: {m['minElevation']:.0f}-{m['maxElevation']:.0f} m")
        info = build_landmask(loc, out)
        print(f"   reserves kept: {[r['name'] for r in info['reserves']]}, water cells: {info['waterCells']}")


if __name__ == "__main__":
    main()
