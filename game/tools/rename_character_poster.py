"""Make a new character concept poster with the actual project title.

Keeps the original artwork unchanged; generated poster is CONCEPT ART,
not a gameplay screenshot or a pack of game-ready 3D models.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


def load_font(size):
    candidates = (
        "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "C:/Windows/Fonts/arialbd.ttf",
    )
    for candidate in candidates:
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def main():
    source = Path("art/characters/the-descent-character-pack.png")
    target = Path("art/characters/downhillroguelite-character-pack.png")
    image = Image.open(source).convert("RGBA")
    width, height = image.size

    # Mask just the old upper-left concept-logo panel with a soft dark fade.
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    pixels = overlay.load()
    for y in range(min(height, 150)):
        for x in range(min(width, 475)):
            horizontal_fade = min(1.0, max(0.0, (475 - x) / 115.0))
            vertical_fade = min(1.0, max(0.0, (150 - y) / 47.0))
            alpha = int(249 * horizontal_fade * vertical_fade)
            if alpha > 0:
                pixels[x, y] = (7, 8, 12, alpha)

    image = Image.alpha_composite(image, overlay)
    draw = ImageDraw.Draw(image)
    headline = load_font(54)
    subtitle = load_font(15)
    draw.text((28, 4), "DOWNHILL", font=headline, fill=(238, 238, 237, 255),
              stroke_width=2, stroke_fill=(13, 13, 15, 255))
    draw.text((28, 55), "ROGUELITE", font=headline, fill=(215, 41, 39, 255),
              stroke_width=2, stroke_fill=(13, 13, 15, 255))
    draw.text((31, 119), "CHARACTER CONCEPTS  /  NPCs, BOSSES & ENEMIES",
              font=subtitle, fill=(235, 235, 235, 255),
              stroke_width=1, stroke_fill=(12, 12, 12, 255))
    # State clearly that this illustration is not a distributable 3D asset pack.
    draw.rectangle((1299, 0, 1437, 103), fill=(10, 11, 15, 255))
    draw.text((1308, 14), "CONCEPT", font=load_font(17),
              fill=(242, 242, 242, 255))
    draw.text((1308, 37), "ART ONLY", font=load_font(17),
              fill=(218, 46, 44, 255))
    draw.text((1308, 70), "NO 3D FILES", font=load_font(12),
              fill=(215, 215, 215, 255))
    draw.rectangle((0, 723, 455, 753), fill=(14, 15, 17, 255))
    draw.text((11, 725), "KONCEPTNI PRIKAZI LIKOVA",
              font=load_font(23), fill=(240, 240, 239, 255))
    draw.rectangle((0, 939, width, height), fill=(12, 13, 15, 255))
    draw.line((0, 939, width, 939), fill=(85, 88, 91, 255), width=2)
    draw.text((30, 951), "CONCEPT ART  /  NOT GAMEPLAY OR 3D ASSETS",
              font=load_font(26), fill=(240, 240, 239, 255))
    draw.text((31, 989),
              "Playable models, rigs, animations and asset packs must be developed separately.",
              font=load_font(17), fill=(180, 180, 185, 255))
    draw.text((1215, 970), "DownhillRoguelite",
              font=load_font(18), fill=(208, 54, 49, 255))
    image.save(target, optimize=True)

    readme_path = Path("../README.md")
    readme = readme_path.read_text(encoding="utf-8")
    readme = readme.replace("art/characters/the-descent-character-pack.png",
                            "art/characters/downhillroguelite-character-pack.png")
    readme = readme.replace("The poster uses an earlier working title, “The Descent”; the actual project's name is **DownhillRoguelite**. ", "")
    readme_path.write_text(readme, encoding="utf-8")


if __name__ == "__main__":
    main()
