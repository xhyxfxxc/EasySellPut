#!/usr/bin/env python3
"""Independent check of an ad-hoc signed .app (rcodesign 0.29's `verify` rejects the empty CMS blob every ad-hoc
signature carries, Apple's codesign included, so it cannot be used here).

For every Mach-O in Contents/MacOS: the embedded CodeDirectory is ad-hoc, every page hash matches the file, and the
special slots match the requirements blob (and, for the main executable, Info.plist and CodeResources). Then every
file listed in _CodeSignature/CodeResources matches its sealed SHA-256 and no file is left unsealed.

usage: verify_sig.py <app>
"""
import base64, hashlib, os, plistlib, struct, sys

CSMAGIC_EMBEDDED = 0xFADE0CC0
CSMAGIC_CODEDIRECTORY = 0xFADE0C02
LC_CODE_SIGNATURE = 0x1D
FAT_MAGIC = 0xCAFEBABE
MH_MAGIC_64 = 0xFEEDFACF
CS_ADHOC = 0x2
HASH_SHA256 = 2


def slices(data):
    if struct.unpack(">I", data[:4])[0] == FAT_MAGIC:
        n = struct.unpack(">I", data[4:8])[0]
        for i in range(n):
            _, _, off, size, _ = struct.unpack(">iiIII", data[8 + i * 20: 28 + i * 20])
            yield data[off: off + size]
    else:
        yield data


def code_directory(macho):
    magic, _, _, _, ncmds = struct.unpack("<IiiII", macho[:20])
    if magic != MH_MAGIC_64:
        raise ValueError("not a 64-bit Mach-O")
    pos = 32
    for _ in range(ncmds):
        cmd, size = struct.unpack("<II", macho[pos: pos + 8])
        if cmd == LC_CODE_SIGNATURE:
            off, length = struct.unpack("<II", macho[pos + 8: pos + 16])
            blob = macho[off: off + length]
            magic, _, count = struct.unpack(">III", blob[:12])
            assert magic == CSMAGIC_EMBEDDED, "no embedded signature superblob"
            entries = {}
            for i in range(count):
                slot, boff = struct.unpack(">II", blob[12 + i * 8: 20 + i * 8])
                entries[slot] = blob[boff:]
            return entries
        pos += size
    raise ValueError("unsigned (no LC_CODE_SIGNATURE)")


def check_macho(path, specials):
    data = open(path, "rb").read()
    for macho in slices(data):
        entries = code_directory(macho)
        cd = entries[0]
        (magic, length, version, flags, hash_off, ident_off, n_special, n_code, code_limit, hash_size, hash_type,
         _, page_shift) = struct.unpack(">IIIIIIIIIBBBB", cd[:40])
        assert magic == CSMAGIC_CODEDIRECTORY
        assert flags & CS_ADHOC, f"{path}: not ad-hoc"
        assert hash_type == HASH_SHA256 and hash_size == 32
        page = 1 << page_shift
        for i in range(n_code):
            want = cd[hash_off + i * 32: hash_off + (i + 1) * 32]
            got = hashlib.sha256(macho[i * page: min((i + 1) * page, code_limit)]).digest()
            assert got == want, f"{path}: page {i} hash mismatch"
        slot = lambda n: cd[hash_off - n * 32: hash_off - (n - 1) * 32]
        if n_special >= 2 and 2 in entries:
            req_len = struct.unpack(">I", entries[2][4:8])[0]
            assert slot(2) == hashlib.sha256(entries[2][:req_len]).digest(), f"{path}: requirements slot mismatch"
        for n, content in specials.items():
            assert n_special >= n and slot(n) == hashlib.sha256(content).digest(), f"{path}: special slot {n} mismatch"
        ident = cd[ident_off: cd.index(b"\0", ident_off)].decode()
    return ident, n_code


def main(app):
    contents = os.path.join(app, "Contents")
    info = open(os.path.join(contents, "Info.plist"), "rb").read()
    resources = open(os.path.join(contents, "_CodeSignature", "CodeResources"), "rb").read()
    exe = plistlib.loads(info)["CFBundleExecutable"]
    macos = os.path.join(contents, "MacOS")
    for f in sorted(os.listdir(macos)):
        specials = {1: info, 3: resources} if f == exe else {}
        ident, pages = check_macho(os.path.join(macos, f), specials)
        print(f"ok  {f}  ({ident}, {pages} pages)")
    sealed = plistlib.loads(resources)["files2"]
    seen = set()
    for rel, entry in sealed.items():
        path = os.path.join(contents, rel)
        if isinstance(entry, dict) and "cdhash" in entry:     # nested code: sealed by its CodeDirectory hash
            seen.add(rel)
            continue
        want = entry["hash2"] if isinstance(entry, dict) else entry
        if isinstance(want, str):
            want = base64.b64decode(want)
        assert hashlib.sha256(open(path, "rb").read()).digest() == want, f"{rel}: sealed hash mismatch"
        seen.add(rel)
    for root, _, files in os.walk(contents):
        for f in files:
            rel = os.path.relpath(os.path.join(root, f), contents)
            top = rel.split(os.sep)[0]
            if top in ("MacOS", "_CodeSignature") or rel in ("Info.plist", "PkgInfo"):
                continue
            assert rel in seen, f"{rel}: not sealed"
    print(f"ok  CodeResources seals {len(sealed)} entries; no unsealed files")


if __name__ == "__main__":
    main(sys.argv[1])
