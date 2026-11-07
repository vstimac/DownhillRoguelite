"""Posts GitHub pushes and releases to Discord as full patch notes (run by .github/workflows/discord.yml).

Release posts end with "Play now" buttons (Windows, Linux) that download the newest build: they
point at GitHub's latest-release links, so even an old post's buttons fetch the current patch.

Discord's own GitHub hook shows only the first ~50 characters of each commit. This posts every
commit's whole message, and a release's whole notes split into one card per section (tables
turned into lines, since Discord can't show tables), with the download links at the end.
Reads the event from GITHUB_EVENT_PATH; the webhook URL comes from the DISCORD_WEBHOOK secret.
Standard library only.
"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

WEBHOOK = os.environ.get("DISCORD_WEBHOOK", "").strip()
EVENT_NAME = os.environ.get("GITHUB_EVENT_NAME", "")
EVENT_PATH = os.environ.get("GITHUB_EVENT_PATH", "")

GOLD = 0xC9963F
EMBER = 0xE8732A
DESCRIPTION_LIMIT = 3900
MESSAGE_LIMIT = 5800
EMBEDS_PER_MESSAGE = 10


# Assets with these names are what the Play now buttons download (the newest full release's).
PLAY_BUTTONS = [
    ("Play now (Windows)", "DownhillRoguelite-playtest1-windows.zip"),
    ("Play now (Linux)", "DownhillRoguelite-linux-beta.tar.gz"),
]


def send(content, embeds, buttons=None):
    """Sends embeds in as many messages as Discord's limits need (10 cards, ~6000 characters each); buttons go on the last."""
    batches = []
    batch = []
    size = 0
    for embed in embeds:
        length = len(embed.get("title", "")) + len(embed.get("description", "")) + sum(len(f["name"]) + len(f["value"]) for f in embed.get("fields", []))
        if batch and (len(batch) >= EMBEDS_PER_MESSAGE or size + length > MESSAGE_LIMIT):
            batches.append(batch)
            batch = []
            size = 0
        batch.append(embed)
        size += length
    if batch:
        batches.append(batch)
    for index, embeds_part in enumerate(batches):
        payload = {"embeds": embeds_part, "username": "Downhill Roguelite", "allowed_mentions": {"parse": []}}
        if index == 0 and content:
            payload["content"] = content
        if buttons and index == len(batches) - 1:
            payload["components"] = [{"type": 1, "components": buttons}]
        post(payload)


def post(payload):
    data = json.dumps(payload).encode("utf-8")
    for attempt in range(5):
        # with_components lets a plain channel webhook send link buttons.
        request = urllib.request.Request(WEBHOOK + "?wait=true&with_components=true", data=data, method="POST")
        request.add_header("Content-Type", "application/json")
        request.add_header("User-Agent", "DownhillRoguelite-Updates (github actions)")
        try:
            with urllib.request.urlopen(request) as response:
                response.read()
                return
        except urllib.error.HTTPError as error:
            if error.code == 429:
                retry = json.loads(error.read() or b"{}").get("retry_after", 2)
                time.sleep(float(retry) + 0.5)
                continue
            print("Discord said", error.code, error.read()[:300], file=sys.stderr)
            if error.code == 400 and "components" in payload:
                # Buttons refused: post the message without them rather than not at all.
                payload = {k: v for k, v in payload.items() if k != "components"}
                data = json.dumps(payload).encode("utf-8")
                continue
            raise
    raise RuntimeError("Discord kept rate-limiting")


def clean_message(message):
    """Drops co-author trailer lines; returns (title, body)."""
    lines = [line for line in message.strip().splitlines() if not line.lower().startswith("co-authored-by:")]
    title = lines[0].strip() if lines else "(no message)"
    body = "\n".join(lines[1:]).strip()
    return title, unwrap(body)


def unwrap(text):
    """Joins hard-wrapped lines back into paragraphs (blank lines and list items stay as they are)."""
    paragraphs = []
    current = []
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped:
            if current:
                paragraphs.append(" ".join(current))
                current = []
            paragraphs.append("")
            continue
        if stripped.startswith(("- ", "* ", "• ")) or re.match(r"^\d+[.)] ", stripped):
            if current:
                paragraphs.append(" ".join(current))
            current = [stripped]
            continue
        current.append(stripped)
    if current:
        paragraphs.append(" ".join(current))
    return re.sub(r"\n{3,}", "\n\n", "\n".join(paragraphs)).strip()


def cut(text, limit):
    if len(text) <= limit:
        return text
    return text[: limit - 1].rstrip() + "…"


def markdown_for_discord(text):
    """Turns release-notes markdown into what Discord shows well: tables become 'key · value' lines."""
    out = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("|"):
            cells = [c.strip() for c in stripped.strip("|").split("|")]
            if all(re.fullmatch(r":?-{2,}:?", c) or c == "" for c in cells):
                continue
            cells = [c for c in cells]
            if len(cells) >= 2 and cells[0] and cells[1]:
                out.append(f"**{cells[0]}** · {' · '.join(cells[1:])}")
            elif any(cells):
                out.append(" · ".join(c for c in cells if c))
            continue
        if stripped == "---":
            continue
        heading = re.match(r"^#{3,6}\s+(.*)", stripped)
        if heading:
            out.append(f"**{heading.group(1)}**")
            continue
        out.append(line)
    text = "\n".join(out)
    return re.sub(r"\n{3,}", "\n\n", text).strip()


def on_push(event):
    commits = event.get("commits") or []
    if not commits:
        return
    branch = event.get("ref", "").replace("refs/heads/", "")
    repo = event.get("repository", {}).get("name", "")
    embeds = []
    for commit in commits:
        title, body = clean_message(commit.get("message", ""))
        embed = {
            "title": cut(title, 250),
            "url": commit.get("url", ""),
            "color": GOLD,
            "author": {"name": commit.get("author", {}).get("name", "")},
            "footer": {"text": f"{repo} · {branch} · {commit.get('id', '')[:7]}"},
            "timestamp": commit.get("timestamp", ""),
        }
        if body:
            embed["description"] = cut(body, DESCRIPTION_LIMIT)
        embeds.append(embed)
    count = len(commits)
    plural = "update" if count == 1 else "updates"
    send(f"**{count} new {plural}** on `{branch}`", embeds)


def on_release(event):
    release = event.get("release", {})
    name = release.get("name") or release.get("tag_name", "Release")
    url = release.get("html_url", "")
    text = markdown_for_discord(release.get("body") or "")
    # One card per "## " section; the text before the first section introduces the release.
    parts = re.split(r"(?m)^##\s+", text)
    embeds = []
    intro = parts[0].strip()
    embeds.append({"title": cut(name, 250), "url": url, "color": EMBER, "description": cut(intro or "New release.", DESCRIPTION_LIMIT)})
    for part in parts[1:]:
        heading, _, rest = part.partition("\n")
        rest = rest.strip()
        if not rest:
            continue
        embeds.append({"title": cut(heading.strip(), 250), "color": GOLD, "description": cut(rest, DESCRIPTION_LIMIT)})
    assets = release.get("assets") or []
    if assets:
        lines = [f"[{a['name']}]({a['browser_download_url']}) ({a['size'] / 1_000_000:.0f} MB)" for a in assets]
        embeds.append({"title": "Downloads", "url": url, "color": EMBER, "description": cut("\n".join(lines), DESCRIPTION_LIMIT)})
    kind = "pre-release" if release.get("prerelease") else "release"
    repo = event.get("repository", {}).get("html_url", "")
    names = {a["name"] for a in assets}
    buttons = []
    for label, asset in PLAY_BUTTONS:
        if asset in names:
            # A full release: the latest-release link always gives the newest patch. A pre-release: its own file.
            link = f"{repo}/releases/latest/download/{asset}"
            if release.get("prerelease"):
                link = f"{url.replace('/tag/', '/download/')}/{asset}"
            buttons.append({"type": 2, "style": 5, "label": label, "url": link})
    buttons.append({"type": 2, "style": 5, "label": "Patch notes", "url": url})
    send(f"**New {kind}: {name}**\n<{url}>", embeds, buttons)


def main():
    if not WEBHOOK:
        print("No DISCORD_WEBHOOK secret; nothing to do.")
        return
    with open(EVENT_PATH, encoding="utf-8") as handle:
        event = json.load(handle)
    if EVENT_NAME == "push":
        on_push(event)
    elif EVENT_NAME == "release":
        on_release(event)


if __name__ == "__main__":
    main()
