#!/usr/bin/env python3
"""Audit the pinned Outposts production and delivery source contracts."""
import hashlib
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

framework, expansion = (pathlib.Path(path) for path in sys.argv[1:3])
source = framework / "Source/Outposts/Outpost"
outpost = (source / "Outpost.cs").read_text(encoding="utf-8-sig")
delivery = (source / "Outpost_Deliveries.cs").read_text(encoding="utf-8-sig")
assert re.search(r"int PawnCount\s*=>\s*occupants\.Count", outpost)
assert re.search(r"void Produce\(\)\s*\{\s*Deliver\(ProducedThings\(\)\);", outpost)
assert re.search(r"void Deliver\(IEnumerable<Thing> items\)\s*\{\s*var things = items\.ToList\(\);", delivery)
assert "1.6" in [node.text for node in ET.parse(expansion / "loadFolders.xml").findall("./v1.7/li")]
town = (expansion / "1.6/Source/VOE/Outpost_Town.cs").read_text(encoding="utf-8-sig")
town_produce = town.split("override void Produce()", 1)[1].split("public override", 1)[0]
assert "AddPawn" in town_produce and "Deliver(" not in town_produce and "base.Produce(" not in town_produce
assert hashlib.sha256((framework / "1.7/Assemblies/Outposts.dll").read_bytes()).hexdigest() == "8324ec9d51706e2d2b1fd75a5862ba8b8c3ed17f0420f6dd39786d4790cb87cf"
assert hashlib.sha256((expansion / "1.6/Assemblies/VOE.dll").read_bytes()).hexdigest() == "5898527ab8f820f0fde5145cfaf197d7fc473714a610af1eb0fad17ad46d74f6"
print("VEF and VOE Outposts contracts: PASS")
