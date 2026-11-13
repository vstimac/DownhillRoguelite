"""
Publishes a play-test release that every installed game picks up by itself (title screen "Update",
"Update game.bat" and the "Let's Play" launcher all read GitHub's release list).

    python -I scripts/publish_release.py <tag> <version> "<title>" [options]

    tag       the release's git tag, e.g. v0.2.9-beta (made on the current commit, which must be pushed)
    version   what players see after updating, e.g. 2.9 (written to version.txt)
    title     the release's name, e.g. "Beta 2.9: the counter clerk"

Options:
    --base DIR      the full release folder updates are made against (default: Beta 2.5's export,
                    see BASE below); its base.txt names the release players have
    --update ZIP    an update zip already made by make_update.py (default: make one now)
    --full ZIP      also attach a whole Windows build (package_windows.py); new players and folders
                    without base.txt take it
    --notes FILE    release notes (Markdown); default: a short line
    --prerelease    publish as a pre-release: the game only offers it with -- --update-prerelease,
                    so the whole path can be tried before players see it
    --draft         upload but leave it a draft (nobody is offered a draft)

What it does, in an order that never offers players a half-uploaded release:
1. Makes the update zip (make_update.py, GODOT must point at the console exe) unless --update is given.
2. Creates the release as a draft on the current commit, uploads the files.
3. Checks every uploaded file's SHA-256 on GitHub against the local file (the game checks it again
   after downloading).
4. Publishes it (unless --draft). From then on the newest release with an update for a player's
   folder is this one.
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO_ROOT = os.path.dirname(ROOT)
BASE = r"D:\Game dev\Builds\DownhillRoguelite-beta-2.5\DownhillRoguelite-playtest1"
OUT = os.path.join(REPO_ROOT, "..", "Builds")


def fail(message):
    print("FAIL  " + message)
    sys.exit(1)


def run(*args, capture=False):
    print("...   " + " ".join(args))
    result = subprocess.run(args, cwd=REPO_ROOT, text=True, capture_output=capture)
    if result.returncode != 0:
        if capture:
            print(result.stdout)
            print(result.stderr)
        fail(f"{args[0]} exited with {result.returncode}")
    return result.stdout if capture else ""


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("tag")
    parser.add_argument("version")
    parser.add_argument("title")
    parser.add_argument("--base", default=BASE)
    parser.add_argument("--update")
    parser.add_argument("--full")
    parser.add_argument("--notes")
    parser.add_argument("--prerelease", action="store_true")
    parser.add_argument("--draft", action="store_true")
    args = parser.parse_args()

    head = run("git", "rev-parse", "HEAD", capture=True).strip()
    remote = run("git", "branch", "-r", "--contains", head, capture=True).strip()
    if not remote:
        fail(f"commit {head[:8]} is not pushed; push it first so the release's tag points at code on GitHub")
    existing = subprocess.run(["gh", "release", "view", args.tag], cwd=REPO_ROOT, capture_output=True, text=True)
    if existing.returncode == 0:
        fail(f"release {args.tag} already exists")

    base_txt = os.path.join(args.base, "base.txt")
    if not os.path.isfile(base_txt):
        fail(f"no base.txt in {args.base}")
    base_id = open(base_txt, encoding="utf-8").read().strip()

    files = []
    update = args.update
    if update is None:
        os.makedirs(OUT, exist_ok=True)
        run(sys.executable, "-I", os.path.join(ROOT, "scripts", "make_update.py"), args.base, args.version, OUT)
        update = os.path.join(OUT, f"DownhillRoguelite-update-{args.version}-for-{base_id}.zip")
    expected_name = f"DownhillRoguelite-update-{args.version}-for-{base_id}.zip"
    if os.path.basename(update) != expected_name:
        fail(f"the update must be named {expected_name} (the game finds it by that name)")
    if not os.path.isfile(update):
        fail(f"no update zip at {update}")
    files.append(update)
    if args.full:
        if not os.path.basename(args.full).endswith("-windows.zip"):
            fail("a whole Windows build must be named *-windows.zip")
        files.append(args.full)

    notes = args.notes
    if notes is None:
        handle, notes = tempfile.mkstemp(suffix=".md")
        with os.fdopen(handle, "w", encoding="utf-8") as f:
            f.write(f"{args.title}\n\nThe game updates itself: start it and pick **Update** on the title screen "
                    f"(or run \"Update game.bat\").\n")

    create = ["gh", "release", "create", args.tag, "--draft", "--target", head, "--title", args.title, "--notes-file", notes]
    if args.prerelease:
        create.append("--prerelease")
    run(*create)
    run("gh", "release", "upload", args.tag, *files)

    print("...   checking the uploaded files on GitHub")
    info = json.loads(run("gh", "release", "view", args.tag, "--json", "assets", capture=True))
    on_github = {a["name"]: a for a in info["assets"]}
    for path in files:
        name = os.path.basename(path)
        asset = on_github.get(name)
        if asset is None:
            fail(f"{name} is missing on GitHub")
        if asset.get("size") != os.path.getsize(path):
            fail(f"{name}: GitHub has {asset.get('size')} bytes, the file has {os.path.getsize(path)}")
        digest = asset.get("digest") or ""
        if digest and digest != "sha256:" + sha256(path):
            fail(f"{name}: GitHub's checksum differs from the file's")
        print(f"PASS  {name} ({os.path.getsize(path) / 1048576:.1f} MB)")

    if args.draft:
        print(f"PASS  draft {args.tag} ready; publish it on GitHub when you want players to get it")
        return
    run("gh", "release", "edit", args.tag, "--draft=false")
    kind = "pre-release (only offered with -- --update-prerelease)" if args.prerelease else "release"
    print(f"PASS  {args.tag} published as a {kind}; installed games offer it on their next start")


if __name__ == "__main__":
    main()
