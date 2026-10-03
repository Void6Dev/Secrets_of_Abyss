# Minimal tModLoader TagIO (big-endian NBT-like, gzip) reader/writer
import gzip, struct, io

BYTE, SHORT, INT, LONG, FLOAT, DOUBLE, BYTES, STRING, LIST, COMPOUND, INTS = 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11


def _rstr(r):
    n = struct.unpack('>h', r.read(2))[0]
    return r.read(n).decode('utf-8')


def _rpayload(r, t):
    if t == BYTE: return ('b', struct.unpack('>b', r.read(1))[0])
    if t == SHORT: return ('s', struct.unpack('>h', r.read(2))[0])
    if t == INT: return ('i', struct.unpack('>i', r.read(4))[0])
    if t == LONG: return ('l', struct.unpack('>q', r.read(8))[0])
    if t == FLOAT: return ('f', struct.unpack('>f', r.read(4))[0])
    if t == DOUBLE: return ('d', struct.unpack('>d', r.read(8))[0])
    if t == BYTES:
        n = struct.unpack('>i', r.read(4))[0]
        return ('B', r.read(n))
    if t == STRING: return ('S', _rstr(r))
    if t == LIST:
        et = r.read(1)[0]
        n = struct.unpack('>i', r.read(4))[0]
        return ('L', et, [_rpayload(r, et) for _ in range(n)])
    if t == COMPOUND:
        d = {}
        while True:
            tt = r.read(1)[0]
            if tt == 0:
                return ('C', d)
            name = _rstr(r)
            d[name] = _rpayload(r, tt)
    if t == INTS:
        n = struct.unpack('>i', r.read(4))[0]
        return ('I', list(struct.unpack('>%di' % n, r.read(4 * n))))
    raise ValueError('tag type %d' % t)


def read(path):
    raw = open(path, 'rb').read()
    try:
        raw = gzip.decompress(raw)
    except OSError:
        pass
    r = io.BytesIO(raw)
    t = r.read(1)[0]
    _rstr(r)
    return _rpayload(r, t), raw


def _wstr(w, s):
    b = s.encode('utf-8')
    w.write(struct.pack('>h', len(b)))
    w.write(b)


TYPE_OF = {'b': BYTE, 's': SHORT, 'i': INT, 'l': LONG, 'f': FLOAT, 'd': DOUBLE, 'B': BYTES, 'S': STRING,
           'L': LIST, 'C': COMPOUND, 'I': INTS}


def _wpayload(w, v):
    k = v[0]
    if k == 'b': w.write(struct.pack('>b', v[1]))
    elif k == 's': w.write(struct.pack('>h', v[1]))
    elif k == 'i': w.write(struct.pack('>i', v[1]))
    elif k == 'l': w.write(struct.pack('>q', v[1]))
    elif k == 'f': w.write(struct.pack('>f', v[1]))
    elif k == 'd': w.write(struct.pack('>d', v[1]))
    elif k == 'B':
        w.write(struct.pack('>i', len(v[1]))); w.write(bytes(v[1]))
    elif k == 'S': _wstr(w, v[1])
    elif k == 'L':
        w.write(bytes([v[1]])); w.write(struct.pack('>i', len(v[2])))
        for e in v[2]: _wpayload(w, e)
    elif k == 'C':
        for name, e in v[1].items():
            w.write(bytes([TYPE_OF[e[0]]])); _wstr(w, name); _wpayload(w, e)
        w.write(b'\x00')
    elif k == 'I':
        w.write(struct.pack('>i', len(v[1]))); w.write(struct.pack('>%di' % len(v[1]), *v[1]))


def dumps(root):
    w = io.BytesIO()
    w.write(bytes([COMPOUND])); _wstr(w, ''); _wpayload(w, root)
    return w.getvalue()


def write(path, root):
    open(path, 'wb').write(gzip.compress(dumps(root)))
