# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "pillow"]
# ///
"""
Composites CC0 ambientCG atlases (single blades, leaves, branches) into game-ready foliage card textures with
alpha, written to Assets/FieldShift/Resources/FieldShift/Textures/:

  Foliage_GrassTuft.png   dense grass tuft built from Foliage001 blades
  Foliage_LeafCluster.png leafy clump built from LeafSet024 leaves (trees, potato, squash)
  Foliage_FirBranch.png   one fir branch cut from LeafSet019
  Foliage_Wheat.png       grain stalks with heads (drawn procedurally)

    uv run Tools/build_foliage.py <folder with the unzipped ambientCG sets>
"""
import os, sys, random, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Assets", "FieldShift", "Resources", "FieldShift", "Textures")
SRC = sys.argv[1]
rnd = random.Random(7)


def atlas(set_id):
    c = Image.open(f"{SRC}/{set_id}/x/{set_id}_1K-PNG_Color.png").convert("RGB")
    o = Image.open(f"{SRC}/{set_id}/x/{set_id}_1K-PNG_Opacity.png").convert("L")
    c.putalpha(o)
    return c


def sprites(img, min_px=400):
    """Split an atlas into its separate pieces by connected alpha regions (bounding boxes)."""
    a = np.asarray(img.split()[-1]) > 40
    h, w = a.shape
    seen = np.zeros_like(a)
    boxes = []
    for y in range(0, h, 3):
        for x in range(0, w, 3):
            if a[y, x] and not seen[y, x]:
                stack = [(y, x)]; seen[y, x] = True
                y0 = y1 = y; x0 = x1 = x; n = 0
                while stack:
                    cy, cx = stack.pop(); n += 1
                    y0, y1, x0, x1 = min(y0, cy), max(y1, cy), min(x0, cx), max(x1, cx)
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and a[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True; stack.append((ny, nx))
                if n >= min_px:
                    boxes.append((x0, y0, x1 + 1, y1 + 1))
    return [img.crop(b) for b in boxes]


def tint(im, factor):
    r, g, b, a = im.split()
    rgb = Image.merge("RGB", (r, g, b))
    arr = np.clip(np.asarray(rgb).astype(np.float32) * np.array(factor), 0, 255).astype(np.uint8)
    out = Image.fromarray(arr); out.putalpha(a)
    return out


def grass_tuft():
    blades = sprites(atlas("Foliage001"))
    W, H = 512, 512
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for i in range(70):
        b = rnd.choice(blades)
        scale = rnd.uniform(0.35, 0.62) * H / b.height
        b = b.resize((max(2, int(b.width * scale * rnd.uniform(0.8, 1.3))), int(b.height * scale)), Image.LANCZOS)
        b = b.rotate(rnd.uniform(-22, 22), resample=Image.BICUBIC, expand=True)
        b = tint(b, (rnd.uniform(0.85, 1.15), rnd.uniform(0.9, 1.15), rnd.uniform(0.75, 1.0)))
        x = int(W / 2 + rnd.gauss(0, W * 0.16) - b.width / 2)
        canvas.alpha_composite(b, (max(0, min(W - b.width, x)), H - b.height))
    canvas.save(os.path.join(OUT, "Foliage_GrassTuft.png"))


def leaf_cluster():
    leaves = sprites(atlas("LeafSet024"))
    W = H = 512
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for i in range(110):
        lf = rnd.choice(leaves)
        s = rnd.uniform(0.13, 0.2) * W / max(lf.size)
        lf = lf.resize((int(lf.width * s), int(lf.height * s)), Image.LANCZOS).rotate(rnd.uniform(0, 360), resample=Image.BICUBIC, expand=True)
        shade = rnd.uniform(0.6, 1.1)
        lf = tint(lf, (shade * 0.95, shade, shade * 0.85))
        ang, rad = rnd.uniform(0, 2 * math.pi), abs(rnd.gauss(0, 0.26)) * W
        x, y = int(W / 2 + math.cos(ang) * rad - lf.width / 2), int(H / 2 + math.sin(ang) * rad * 0.85 - lf.height / 2)
        if 0 <= x <= W - lf.width and 0 <= y <= H - lf.height:
            canvas.alpha_composite(lf, (x, y))
    canvas.save(os.path.join(OUT, "Foliage_LeafCluster.png"))


def fir_branch():
    branches = sorted(sprites(atlas("LeafSet019")), key=lambda im: -im.width * im.height)
    b = branches[0]
    W, H = 1024, 512
    s = min(W / b.width, H / b.height) * 0.98
    b = b.resize((int(b.width * s), int(b.height * s)), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(b, ((W - b.width) // 2, (H - b.height) // 2))
    canvas.save(os.path.join(OUT, "Foliage_FirBranch.png"))


def wheat():
    W, H = 512, 1024
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for i in range(46):
        x0 = rnd.uniform(20, W - 20)
        top = rnd.uniform(40, 260)
        lean = rnd.uniform(-40, 40)
        stem = (196 + rnd.randint(-20, 15), 160 + rnd.randint(-20, 15), 80 + rnd.randint(-15, 15), 255)
        pts = [(x0 + lean * (1 - t) ** 2 * 0 + lean * t * t, H - t * (H - top)) for t in np.linspace(0, 1, 12)]
        d.line(pts, fill=stem, width=3)
        hx, hy = pts[-1]
        # the grain head: overlapping kernels with whiskers (awns)
        for k in range(14):
            ky = hy - k * 7
            for side in (-1, 1):
                kx = hx + side * 4 + lean * 0.02 * k
                col = (222 + rnd.randint(-20, 20), 182 + rnd.randint(-20, 15), 92 + rnd.randint(-20, 20), 255)
                d.ellipse([kx - 4, ky - 6, kx + 4, ky + 6], fill=col)
                d.line([(kx, ky), (kx + side * 10, ky - 22)], fill=(230, 200, 120, 200), width=1)
        # a couple of leaves
        for _ in range(2):
            ly = rnd.uniform(H * 0.45, H * 0.85)
            lx = x0 + lean * ((H - ly) / (H - top)) ** 2
            side = rnd.choice((-1, 1))
            d.line([(lx, ly), (lx + side * 40, ly - 60), (lx + side * 70, ly - 50)], fill=(180, 160, 80, 255), width=4)
    img = img.filter(ImageFilter.SMOOTH)
    img.save(os.path.join(OUT, "Foliage_Wheat.png"))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    grass_tuft(); leaf_cluster(); fir_branch(); wheat()
    print("foliage textures written to", OUT)
