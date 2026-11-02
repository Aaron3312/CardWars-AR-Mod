# Generates the versus table marker: an original, high-contrast, non-repeating, rotation-asymmetric image.
import math, random, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

out = sys.argv[1]
N = 1024
rng = random.Random(20261031)
img = Image.new('L', (N, N), 255)
d = ImageDraw.Draw(img)

# 1. Irregular recursive split into tiles with varied greys (corners everywhere, no repeats).
def split(x0, y0, x1, y1, depth):
    w, h = x1 - x0, y1 - y0
    if depth > 5 or (w < 90 or h < 90) or (depth > 2 and rng.random() < 0.18):
        g = rng.choice([0, 0, 25, 60, 110, 170, 220, 255, 255])
        d.rectangle([x0, y0, x1, y1], fill=g)
        return
    if w > h * rng.uniform(0.7, 1.4):
        cx = int(x0 + w * rng.uniform(0.3, 0.7))
        split(x0, y0, cx, y1, depth + 1); split(cx, y0, x1, y1, depth + 1)
    else:
        cy = int(y0 + h * rng.uniform(0.3, 0.7))
        split(x0, y0, x1, cy, depth + 1); split(x0, cy, x1, y1, depth + 1)
split(0, 0, N, N, 0)

# 2. Scattered polygons, stars and rings of contrasting tone.
for i in range(260):
    x, y = rng.uniform(0, N), rng.uniform(0, N)
    r = rng.uniform(10, 46)
    g = rng.choice([0, 255, 0, 255, 40, 200])
    kind = rng.random()
    if kind < 0.45:
        k = rng.randint(3, 7)
        a0 = rng.uniform(0, math.pi)
        pts = [(x + r * rng.uniform(0.5, 1.0) * math.cos(a0 + 2 * math.pi * j / k), y + r * rng.uniform(0.5, 1.0) * math.sin(a0 + 2 * math.pi * j / k)) for j in range(k)]
        d.polygon(pts, fill=g)
    elif kind < 0.7:
        d.ellipse([x - r, y - r, x + r, y + r], outline=g, width=max(3, int(r / 4)))
    elif kind < 0.85:
        k = rng.randint(4, 6)
        pts = []
        for j in range(2 * k):
            rr = r if j % 2 == 0 else r * 0.45
            a = 2 * math.pi * j / (2 * k)
            pts.append((x + rr * math.cos(a), y + rr * math.sin(a)))
        d.polygon(pts, fill=g)
    else:
        a = rng.uniform(0, math.pi)
        dx, dy = math.cos(a) * r * 1.6, math.sin(a) * r * 1.6
        d.line([x - dx, y - dy, x + dx, y + dy], fill=g, width=rng.randint(4, 9))

# 3. A large asymmetric glyph so the orientation is never ambiguous.
d.polygon([(70, 830), (70, 960), (330, 960), (330, 900), (150, 900), (150, 830)], fill=0)
d.polygon([(78, 838), (78, 952), (322, 952), (322, 908), (142, 908), (142, 838)], fill=255)
d.ellipse([820, 70, 960, 210], fill=0)
d.ellipse([850, 100, 930, 180], fill=255)
d.ellipse([873, 123, 907, 157], fill=0)

# 4. A thick black frame with a notch on the bottom edge (the host's edge).
B = 28
d.rectangle([0, 0, N - 1, B], fill=0); d.rectangle([0, N - 1 - B, N - 1, N - 1], fill=0)
d.rectangle([0, 0, B, N - 1], fill=0); d.rectangle([N - 1 - B, 0, N - 1, N - 1], fill=0)
d.rectangle([N // 2 - 60, N - 1 - B, N // 2 + 60, N - 1], fill=255)

img = img.filter(ImageFilter.SMOOTH)
rgb = Image.merge('RGB', (img, img, img))
# A touch of colour for people; trackers use luminance.
px = rgb.load()
for y in range(0, N):
    for x in range(0, N):
        v = px[x, y][0]
        if 90 < v < 140:
            px[x, y] = (min(255, v + 70), max(0, v - 30), max(0, v - 50))
        elif 150 < v < 200:
            px[x, y] = (max(0, v - 80), max(0, v - 20), min(255, v + 40))
rgb.save(out)
print('saved', out)
