"""Manually reviewed, literal test-harness -> application-source witnesses.

The tuple list is NOT a source-code search or behavioural test coverage claim.
Each witness was read and pinned at REVIEWED_COMMIT. Insert only when both
endpoint blobs and the exact witness line still match. Never revive an existing
stale edge. Any future review must update the pins and the witness deliberately.
"""
import hashlib

REVIEWED_COMMIT = "a47693dbe329dd95b227f341a18b3f171b2ff93e"
# (harness path, application path, harness blob, application blob, line, exact source line)
WITNESSES = (
    ("tests/AiStartupChecks/run.py", "IRSpeedyVPN/Services/TunnelPlusService.cs",
     "9e552b21bd8a108512f4648cf921cf803f2afba3", "0aea4e047b39c35fd2b6026274d6bdb997459a13", 12,
     "source = (root / 'IRSpeedyVPN/Services/TunnelPlusService.cs').read_text(encoding='utf-8-sig')"),
    ("tests/LastSuccessfulServerChecks/run.py", "IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs",
     "b0b311bf798f23382c4006d13663a87adeefcfbb", "99351bde81910efd40ce1a484446165e580af3a2", 5,
     "source=(root/'IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs').read_text()"),
    ("tests/LastSuccessfulServerChecks/run.py", "IRSpeedyVPN/Common/LastSuccessfulServer.cs",
     "b0b311bf798f23382c4006d13663a87adeefcfbb", "649599157f8b8aa2db3822be6a4ee68607471585", 85,
     "(p/'LastSuccessfulServer.cs').write_text((root/'IRSpeedyVPN/Common/LastSuccessfulServer.cs').read_text())"),
    ("tests/LastSuccessfulServerChecks/run.py", "IRSpeedyVPN/Components/ServerListControl/ServerListRanking.cs",
     "b0b311bf798f23382c4006d13663a87adeefcfbb", "ae29a310ca4e6230eba82547f88a95463907ee09", 86,
     "(p/'ServerListRanking.cs').write_text((root/'IRSpeedyVPN/Components/ServerListControl/ServerListRanking.cs').read_text())"),
    ("tests/ConnectionTestChecks/run.py", "IRSpeedyVPN/UserControls/UCUserInfo.ConnectionTest.cs",
     "5abbc19523cb7439aa80ee7d7491eb78acfabb6f", "bbeb05732ac27d6bfb7fb67a7f9e5e9e47aeb899", 149,
     "(path / 'ConnectionTest.cs').write_text((root / 'IRSpeedyVPN/UserControls/UCUserInfo.ConnectionTest.cs').read_text())"),
    ("tests/ConnectionTestChecks/run.py", "IRSpeedyVPN/UserControls/UCUserInfo.NetworkRecovery.cs",
     "5abbc19523cb7439aa80ee7d7491eb78acfabb6f", "71714ee4b615f6c3122532f95adf67db4d56c83e", 150,
     "(path / 'NetworkRecoveryUi.cs').write_text((root / 'IRSpeedyVPN/UserControls/UCUserInfo.NetworkRecovery.cs').read_text())"),
    ("tests/ConnectionTestChecks/run.py", "IRSpeedyVPN/Services/NetworkRecoverySession.cs",
     "5abbc19523cb7439aa80ee7d7491eb78acfabb6f", "2b13a06f1dc9d6288e741e0e613c9ac127563c9b", 151,
     "(path / 'NetworkRecoverySession.cs').write_text((root / 'IRSpeedyVPN/Services/NetworkRecoverySession.cs').read_text())"),
    ("tests/DeviceLimitChecks/run.py", "IRSpeedyVPN/MainWindow.xaml.cs",
     "587d09d3b2e7be1ead7f90486e30cf0b04413ce0", "62b03e8486c6fe43e659f2ce8cffb92bdeb704f2", 14,
     "main = (root / 'IRSpeedyVPN/MainWindow.xaml.cs').read_text()"),
    ("tests/DeviceLimitChecks/run.py", "IRSpeedyVPN/Windows/DeviceLimitWindow.xaml",
     "587d09d3b2e7be1ead7f90486e30cf0b04413ce0", "9a8d31517aae279d896f65899d4ccad069a7f8be", 16,
     "xaml = ET.parse(root / 'IRSpeedyVPN/Windows/DeviceLimitWindow.xaml')"),
    ("tests/ResidentialChecks/run-cache.py", "IRSpeedyVPN/Services/ServerCheckCache.cs",
     "238448eb761f954f387f70b707121e5318157baa", "3a18ff6c1aeb1c0740eff0c8e8206fccaad921d5", 17,
     "'Residential/ResidentialService.cs', 'Services/ServerCheckCache.cs',"),
    ("tests/ServerListTransitionChecks/run.py", "IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs",
     "f9b0fda8bfb23c08ce6687e117c3d334d6c646eb", "d977a0334d1a09cd6475efd36a4f887793756ce1", 278,
     "animator=(root/'IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs').read_text()"),
)


def seed_reviewed_test_links(git, manifest, target, report):
    """Add reviewed, hash-pinned test-source links once; never reverify old links."""
    inventory = manifest["file_inventory"]
    existing = {(r["from"], r["to"], r["kind"]): r for r in manifest["relationships"]}
    checked = {}
    inserted = 0
    skipped = []
    for source, dest, source_sha, target_sha, line, witness_text in WITNESSES:
        key = (source, dest, "reads_source")
        if key in existing:
            # The updater's earlier invalidation remains authoritative, even
            # if a matching witness happens to survive a semantic change.
            continue
        if inventory.get(source) != source_sha or inventory.get(dest) != target_sha:
            skipped.append(source + " -> " + dest + " (endpoint changed; requires manual review)")
            continue
        if source_sha not in checked:
            checked[source_sha] = git.text(source_sha, source).splitlines()
        lines = checked[source_sha]
        if not (0 < line <= len(lines)) or lines[line - 1].strip() != witness_text:
            raise ValueError("Reviewed test witness no longer matches: " + source)
        rid = "T" + hashlib.sha256(("\0".join(key)).encode()).hexdigest()[:16]
        if any(r["id"] == rid for r in manifest["relationships"]):
            raise ValueError("Duplicate reviewed test relation ID: " + rid)
        relation = {
            "id": rid, "module": "tests", "kind": "reads_source",
            "from": source, "to": dest,
            "source_symbol": None, "target_symbol": None,
            "evidence": {
                "path": source, "line": line, "text": witness_text,
                "commit": REVIEWED_COMMIT, "blob": source_sha,
            },
            "origin": "reviewed-test-witness",
            "verification": "literal-harness-file-read-only",
            "status": "verified", "source_blob": source_sha,
            "target_blob": target_sha, "last_verified_commit": REVIEWED_COMMIT,
            "note": "Harness references or reads source; not proof of assertion/behaviour coverage.",
        }
        manifest["relationships"].append(relation)
        existing[key] = relation
        inserted += 1
    report["new_reviewed_test_relations"] = inserted
    report["test_links_pending_re_review"] = skipped
