# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "pillow", "shapely"]
# ///
"""
Builds the location-select map: NASA Blue Marble shaded relief (via NASA GIBS) of British Columbia with the
province highlighted. Output: Assets/FieldShift/Resources/FieldShift/UI/bc_map.jpg + bc_map.json.

    uv run Tools/build_bc_map.py
"""
import io, json, os, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import shapely
from shapely.geometry import shape

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Assets", "FieldShift", "Resources", "FieldShift", "UI")
S, N, W, E = 47.6, 60.6, -140.2, -112.6
WIDTH, HEIGHT = 2400, 1920          # pixel aspect squashes longitude by ~cos(54°) so BC keeps its shape


def curl(url):
    return subprocess.run(["curl", "-sSfL", "--max-time", "180", "-A", "FieldShift-hackathon/0.1", url], check=True, capture_output=True).stdout


def main():
    os.makedirs(OUT, exist_ok=True)
    wms = ("https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0"
           f"&LAYERS=BlueMarble_ShadedRelief_Bathymetry&STYLES=&CRS=EPSG:4326&BBOX={S},{W},{N},{E}"
           f"&WIDTH={WIDTH}&HEIGHT={HEIGHT}&FORMAT=image/jpeg")
    img = Image.open(io.BytesIO(curl(wms))).convert("RGB")
    ne = json.loads(curl("https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_1_states_provinces.geojson"))
    bc = shape(next(f for f in ne["features"] if f["properties"].get("name") == "British Columbia")["geometry"])

    # pixel centres -> lon/lat
    xs = W + (np.arange(WIDTH) + 0.5) / WIDTH * (E - W)
    ys = N - (np.arange(HEIGHT) + 0.5) / HEIGHT * (N - S)
    X, Y = np.meshgrid(xs, ys)
    inside = shapely.contains_xy(bc, X, Y)

    a = np.asarray(img).astype(np.float32) / 255
    grey = a.mean(axis=2, keepdims=True)
    outside = (a * 0.35 + grey * 0.65) * 0.38                      # dim, desaturated neighbours
    ins = np.clip(np.clip((a - 0.5) * 1.1 + 0.5, 0, 1) ** 0.8 * 1.12, 0, 1)   # brighter, a little more contrast inside BC
    m = inside[..., None].astype(np.float32)
    m = np.asarray(Image.fromarray((m[..., 0] * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))).astype(np.float32)[..., None] / 255
    out = Image.fromarray((np.clip(ins * m + outside * (1 - m), 0, 1) * 255).astype(np.uint8))

    # province outline with a soft shadow
    def to_px(lon, lat):
        return ((lon - W) / (E - W) * WIDTH, (N - lat) / (N - S) * HEIGHT)
    polys = list(bc.geoms) if bc.geom_type == "MultiPolygon" else [bc]
    shadow = Image.new("L", out.size, 0)
    ds = ImageDraw.Draw(shadow)
    lines = Image.new("RGBA", out.size, (0, 0, 0, 0))
    dl = ImageDraw.Draw(lines)
    for p in polys:
        if p.area < 0.002:
            continue
        ring = [to_px(x, y) for x, y in p.exterior.coords]
        ds.line(ring, fill=200, width=9)
        dl.line(ring, fill=(255, 255, 255, 235), width=3)
    shadow = shadow.filter(ImageFilter.GaussianBlur(6))
    out = Image.composite(Image.new("RGB", out.size, (0, 0, 0)), out, shadow.point(lambda v: int(v * 0.55)))
    out.paste(lines, (0, 0), lines)
    out.save(os.path.join(OUT, "bc_map.jpg"), quality=88)
    json.dump(dict(source="NASA GIBS Blue Marble Shaded Relief and Bathymetry (NASA Earth Observatory); BC boundary: Natural Earth (public domain)",
                   south=S, north=N, west=W, east=E, width=WIDTH, height=HEIGHT),
              open(os.path.join(OUT, "bc_map.json"), "w"), indent=1)
    print("wrote", os.path.join(OUT, "bc_map.jpg"))


if __name__ == "__main__":
    main()
