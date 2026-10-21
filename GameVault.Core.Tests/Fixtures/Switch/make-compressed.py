# The test files of SwitchCompressedTests: .nsz and .xcz written as the nsz tool writes them (checked with its own
# decompressor). Run: python make-compressed.py <folder> (needs pycryptodome and zstandard).
import os, sys, hashlib, struct, zstandard
from Crypto.Cipher import AES
from Crypto.Util import Counter
out = sys.argv[1]
rnd = __import__('random').Random(7)
def rb(n): return bytes(rnd.getrandbits(8) for _ in range(n))
def ctr(key, nonce, offset, data):
    c = AES.new(key, AES.MODE_CTR, counter=Counter.new(64, prefix=nonce[:8], initial_value=offset >> 4))
    return c.encrypt(data)
def make_nca(block):
    header = rb(0x4000)
    # section 0 starts after a gap (plain), section 1 encrypted (type 3), section 2 not (type 1)
    secs = [(0x6000, 0x23450, 3), (0x29450, 0x3010, 1)]
    key, nonce = rb(16), rb(16)
    plain = {}
    body = bytearray()
    gap = bytes(range(256)) * (0x2000 // 256)
    body += gap
    enc = bytearray(header + gap)
    for off, size, t in secs:
        p = (b"SanctuaryVault " * (size // 15 + 1))[:size]
        p = bytes(a ^ b for a, b in zip(p, rb(size))) if t == 1 else p
        body += p
        enc += ctr(key, nonce, off, p) if t == 3 else p
    nca = bytes(enc)
    name = hashlib.sha256(nca).hexdigest()[:32]
    ncz = bytearray(header) + b"NCZSECTN" + struct.pack("<q", len(secs))
    for off, size, t in secs:
        ncz += struct.pack("<qqqq", off, size, t, 0) + key + nonce
    if block:
        exp = 14; bs = 1 << exp
        blocks = [bytes(body[i:i+bs]) for i in range(0, len(body), bs)]
        comp = [zstandard.ZstdCompressor().compress(b) for b in blocks]
        # the second block stored as it is (as when compressing does not help)
        comp[1] = blocks[1]
        comp = [c if len(c) < len(b) else b for c, b in zip(comp, blocks)]
        ncz += b"NCZBLOCK" + bytes([2, 1, 0, exp]) + struct.pack("<iq", len(blocks), len(body))
        ncz += b"".join(struct.pack("<i", len(c)) for c in comp) + b"".join(comp)
    else:
        ncz += zstandard.ZstdCompressor(level=3).compress(bytes(body))
    return name, nca, bytes(ncz)
def pfs0(files):
    strings = b"".join(n.encode() + b"\0" for n, _ in files)
    head = b"PFS0" + struct.pack("<iii", len(files), len(strings), 0)
    off = 0; at = 0; ent = b""
    for n, d in files:
        ent += struct.pack("<qqii", off, len(d), at, 0); off += len(d); at += len(n) + 1
    return head + ent + strings + b"".join(d for _, d in files)
def hfs0(files):
    strings = b"".join(n.encode() + b"\0" for n, _ in files)
    head = b"HFS0" + struct.pack("<iii", len(files), len(strings), 0)
    off = 0; at = 0; ent = b""
    for n, d in files:
        ent += struct.pack("<qqiiq", off, len(d), at, 0x200, 0) + hashlib.sha256(d[:0x200]).digest(); off += len(d); at += len(n) + 1
    return head + ent + strings + b"".join(d for _, d in files)
n1, nca1, ncz1 = make_nca(False)
n2, nca2, ncz2 = make_nca(True)
tik = rb(0x2C0)
for n, d in [(n1 + ".nca", nca1), (n2 + ".nca", nca2), (n1 + ".ncz", ncz1), (n2 + ".ncz", ncz2)]:
    open(os.path.join(out, n), "wb").write(d)
open(os.path.join(out, "game.nsz"), "wb").write(pfs0([(n1 + ".ncz", ncz1), ("0100000000001000.tik", tik), (n2 + ".ncz", ncz2)]))
open(os.path.join(out, "expected.nsp.files"), "w").write("\n".join([n1 + ".nca", "0100000000001000.tik", n2 + ".nca"]))
secure = hfs0([(n1 + ".ncz", ncz1), (n2 + ".ncz", ncz2)])
root = hfs0([("update", hfs0([])), ("normal", hfs0([])), ("secure", secure)])
xci = bytearray(0x200); xci[0x100:0x104] = b"HEAD"; xci[0x130:0x138] = struct.pack("<q", 0x200); xci[0x138:0x140] = struct.pack("<q", 0x200)
open(os.path.join(out, "card.xcz"), "wb").write(bytes(xci) + root)
open(os.path.join(out, "tik.bin"), "wb").write(tik)
print(n1, n2)
