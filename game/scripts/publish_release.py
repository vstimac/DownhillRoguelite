"""
Publishes a play-test release that every installed game picks up by itself (title screen "Update",
"Update game.bat" and the "Let's Play" launcher all read GitHub's release list).

    python -I scripts/publish_release.py <tag> <version> "<title>" [options]

    tag       the release's git tag, e.g. v0.2.9-beta (made on the current commit, which must be pushed)
    version   what players see after updating, e.g. 2.9 (written to version.txt)
    title     the release's name, e.g. "Beta 2.9: the counter clerk"

Options:
    --base DIR      the full release folder updates are made against (default: Beta 2.9's export,
                    see BASE below); its base.txt names the release players have
    --update ZIP    an update zip already made by make_update.py (default: make one now)
    --also-base DIR another, older Windows base that players may still have (default: none, see
                    OLD_BASES): an update is made and attached for it too. Folders on a base with no
                    update attached take the whole build instead, so --full covers them
    --also-update ZIP   such an update already made by make_update.py (repeatable)
    --no-old-bases  attach only the update for --base
    --full ZIP      also attach a whole Windows build (package_windows.py); new players, folders
                    without base.txt and folders on older bases take it. Required for a major version
    --linux-base DIR    a Linux full release folder (build_linux.sh's stage/ with base.txt): also make
                        and attach the small Linux update against it (default: Beta 2.9's, LINUX_BASE)
    --linux-update TAR  a Linux update already made by make_update.py --platform linux
    --linux-full TAR    also attach a whole Linux build (*-linux*.tar.gz from build_linux.sh): Linux
                        players without a matching base take it, and it becomes their new base
    --notes FILE    release notes (Markdown); default: a short line
    --prerelease    publish as a pre-release: the game only offers it with -- --update-prerelease,
                    so the whole path can be tried before players see it
    --draft         upload but leave it a draft (nobody is offered a draft)

Versions: a major version (3.0, 4.0, ...) is a full release: it must attach --full and --linux-full,
which become the new bases (point BASE and LINUX_BASE at their folders afterwards), plus small
updates for the previous base so its players still download only a few MB. Minor versions (3.1,
3.2, ...) are small updates on the newest base.

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
# Beta 2.9 (2026-11-14) is the newest full release for both systems: the README's download buttons give it.
BASE = r"D:\Game dev\Builds\DownhillRoguelite-beta-2.9\DownhillRoguelite-playtest1"
LINUX_BASE = r"D:\Game dev\Builds\DownhillRoguelite-linux-2.9\stage"
# Players who downloaded Beta 2.5 to 2.8 still have 2.5 as their base: they get an update too.
# Older bases that still get small updates. Empty since Beta 3.0: folders on 2.5 take the whole build.
OLD_BASES = []
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
    parser.add_argument("--also-base", action="append", default=None)
    parser.add_argument("--also-update", action="append", default=[])
    parser.add_argument("--no-old-bases", action="store_true")
    parser.add_argument("--linux-base", default=LINUX_BASE)
    parser.add_argument("--linux-update")
    parser.add_argument("--linux-full")
    parser.add_argument("--notes")
    parser.add_argument("--prerelease", action="store_true")
    parser.add_argument("--draft", action="store_true")
    args = parser.parse_args()

    if args.version.endswith(".0") and (not args.full or not args.linux_full):
        fail(f"{args.version} is a major version: attach the whole builds too (--full and --linux-full); they become the new bases")

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

    # The same version for older Windows bases.
    also_updates = list(args.also_update)
    also_bases = args.also_base
    if also_bases is None:
        also_bases = OLD_BASES
    if args.no_old_bases:
        also_bases = []
    for old_base in also_bases:
        old_txt = os.path.join(old_base, "base.txt")
        if not os.path.isfile(old_txt):
            fail(f"no base.txt in {old_base}")
        old_id = open(old_txt, encoding="utf-8").read().strip()
        old_name = f"DownhillRoguelite-update-{args.version}-for-{old_id}.zip"
        if any(os.path.basename(path) == old_name for path in also_updates):
            continue
        os.makedirs(OUT, exist_ok=True)
        run(sys.executable, "-I", os.path.join(ROOT, "scripts", "make_update.py"), old_base, args.version, OUT)
        also_updates.append(os.path.join(OUT, old_name))
    for path in also_updates:
        name = os.path.basename(path)
        if not (name.startswith(f"DownhillRoguelite-update-{args.version}-for-") and name.endswith(".zip")):
            fail(f"{name}: an update for an older base must be named DownhillRoguelite-update-{args.version}-for-<base>.zip")
        if not os.path.isfile(path):
            fail(f"no update zip at {path}")
        if path not in files:
            files.append(path)
    if args.full:
        if not os.path.basename(args.full).endswith("-windows.zip"):
            fail("a whole Windows build must be named *-windows.zip")
        files.append(args.full)

    # Linux: its own small update (made against a Linux base) and, when given, a whole build.
    linux_update = args.linux_update
    if args.linux_base:
        linux_base_txt = os.path.join(args.linux_base, "base.txt")
        if not os.path.isfile(linux_base_txt):
            fail(f"no base.txt in {args.linux_base}")
        linux_base_id = open(linux_base_txt, encoding="utf-8").read().strip()
        linux_name = f"DownhillRoguelite-update-{args.version}-linux-for-{linux_base_id}.tar.gz"
        if linux_update is None:
            run(sys.executable, "-I", os.path.join(ROOT, "scripts", "make_update.py"), args.linux_base, args.version, OUT, "--platform", "linux")
            linux_update = os.path.join(OUT, linux_name)
        if os.path.basename(linux_update) != linux_name:
            fail(f"the Linux update must be named {linux_name} (the game finds it by that name)")
    if linux_update:
        name = os.path.basename(linux_update)
        if not (name.startswith("DownhillRoguelite-update-") and "-linux-for-" in name and name.endswith(".tar.gz")):
            fail("a Linux update must be named DownhillRoguelite-update-<version>-linux-for-<base>.tar.gz")
        if not os.path.isfile(linux_update):
            fail(f"no Linux update at {linux_update}")
        files.append(linux_update)
    if args.linux_full:
        name = os.path.basename(args.linux_full)
        if name.startswith("DownhillRoguelite-update-") or "-linux" not in name or not name.endswith(".tar.gz"):
            fail("a whole Linux build must be named *-linux*.tar.gz")
        if not os.path.isfile(args.linux_full):
            fail(f"no Linux build at {args.linux_full}")
        files.append(args.linux_full)

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
