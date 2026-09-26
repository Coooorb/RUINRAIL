#!/usr/bin/env python3
"""Cheap documentation structure check. No Unity, no dependencies. Exit 1 on any problem.

    python3 scripts/check-docs.py

Checks: CLAUDE.md present and concise; no stray Markdown at the repo root; no task/prompt/report files outside the
archives; internal Markdown links resolve; every current doc under docs/ is reachable from docs/README.md; only one
current release-status document; archived docs that sound current carry a SUPERSEDED/HISTORICAL banner; every skill has
name/description frontmatter.
"""
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SKIP_DIRS = {".git", "Library", "Temp", "Logs", "Builds", "UserSettings", "TestResults", "Packages", "Assets",
             "ProjectSettings", "obj", ".vscode", ".idea"}
ARCHIVES = ("production/archive/", "docs/archive/", ".claude/work/", ".claude/memory/")
ROOT_MD_ALLOWED = {"CLAUDE.md", "README.md"}
PRODUCTION_TOP_ALLOWED = {"CURRENT_RELEASE_STATUS.md", "VISUAL_SLICE_APPROVAL.md", "137_PROTOTYPE_VALUE_DISPOSITIONS.md"}
TASKLIKE = re.compile(r"(^TASK_.*|.*PROMPT.*|^RUN_TASK.*|.*_(FIX|IMPLEMENTATION|PASS)?_?REPORT)\.md$", re.I)
LINK = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")
CLAUDE_MAX_LINES = 150

problems = []


def rel(path):
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def markdown_files():
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if name.endswith(".md"):
                yield rel(os.path.join(dirpath, name))


def archived(path):
    return path.startswith(ARCHIVES)


files = sorted(markdown_files())

claude = os.path.join(ROOT, "CLAUDE.md")
if not os.path.isfile(claude):
    problems.append("CLAUDE.md is missing")
elif sum(1 for _ in open(claude, encoding="utf-8")) > CLAUDE_MAX_LINES:
    problems.append(f"CLAUDE.md exceeds {CLAUDE_MAX_LINES} lines; move detail to docs/DEVELOPMENT_WORKFLOW.md")

for f in files:
    name = os.path.basename(f)
    if "/" not in f and name not in ROOT_MD_ALLOWED:
        problems.append(f"stray Markdown at repo root: {f}")
    if not archived(f) and TASKLIKE.match(name):
        problems.append(f"task/prompt/report file outside an archive: {f} (git is the history; use .claude/work/ for temp)")
    if f.startswith("production/") and f.count("/") == 1 and name not in PRODUCTION_TOP_ALLOWED:
        problems.append(f"unexpected top-level production doc: {f} (current status belongs in CURRENT_RELEASE_STATUS.md; history in archive/)")

    text = open(os.path.join(ROOT, f), encoding="utf-8", errors="replace").read()
    if not archived(f):
        for target in LINK.findall(text):
            if re.match(r"^[a-z]+:", target) or target.startswith("#"):
                continue
            path = target.split("#", 1)[0]
            if path and not os.path.exists(os.path.normpath(os.path.join(ROOT, os.path.dirname(f), path))):
                problems.append(f"broken link in {f}: {target}")
    elif f.startswith("production/archive/"):
        head = "\n".join(text.splitlines()[:10])
        if re.search(r"\bcurrent (release|production) (status|state)\b", head, re.I) and not re.search(r"SUPERSEDED|HISTORICAL", head):
            problems.append(f"archived doc presents itself as current without a SUPERSEDED/HISTORICAL banner: {f}")

index_path = os.path.join(ROOT, "docs", "README.md")
index = open(index_path, encoding="utf-8").read() if os.path.isfile(index_path) else ""
if not index:
    problems.append("docs/README.md (documentation index) is missing")
for f in files:
    if f.startswith("docs/") and not archived(f) and f != "docs/README.md":
        inner = f[len("docs/"):]
        parent = os.path.dirname(inner) + "/"
        if inner not in index and f"]({parent})" not in index:  # a file, or its folder linked as a whole
            problems.append(f"permanent doc not indexed in docs/README.md: {f}")

status_like = [f for f in files if not archived(f) and re.search(r"(RELEASE_STATUS|CURRENT_STATE|RELEASE_CANDIDATE_AUDIT)", f)]
if sorted(status_like) != ["docs/CURRENT_STATE.md", "production/CURRENT_RELEASE_STATUS.md"]:
    problems.append("expected exactly docs/CURRENT_STATE.md and production/CURRENT_RELEASE_STATUS.md as current status docs, found: "
                    + ", ".join(status_like))

skills = os.path.join(ROOT, ".claude", "skills")
for skill in sorted(os.listdir(skills)) if os.path.isdir(skills) else []:
    path = os.path.join(skills, skill, "SKILL.md")
    if not os.path.isfile(path):
        problems.append(f"skill without SKILL.md: .claude/skills/{skill}")
        continue
    head = open(path, encoding="utf-8").read().split("---")
    meta = head[1] if len(head) > 2 and head[0].strip() == "" else ""
    if not re.search(rf"^name:\s*{re.escape(skill)}\s*$", meta, re.M) or not re.search(r"^description:\s*\S", meta, re.M):
        problems.append(f"skill frontmatter needs name: {skill} and a description: .claude/skills/{skill}/SKILL.md")

if problems:
    print("DOCS CHECK: FAIL")
    for p in problems:
        print("  - " + p)
    sys.exit(1)
print(f"DOCS CHECK: PASS — {len(files)} Markdown files, {sum(1 for f in files if not archived(f))} current")
