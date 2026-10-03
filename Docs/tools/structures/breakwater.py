# Builds Assets/Structures/breakwater_ruin.str by code, after the concept art.
# Left = world edge, sea to the right (mirrored by worldgen for the right ocean).
# Row WATERLINE is the first underwater row: keep it in sync with
# TideOceanPass.BreakwaterWaterlineRow.
#
#   python breakwater.py <out.str>      — writes the file and prints an ASCII preview
import math, random, sys
import tagio

W, H = 80, 50
WATERLINE = 32
DECK = 30                 # walkable deck surface row (blocks start here)
rng = random.Random(1907)

# --- Materials --------------------------------------------------------------
# "n:" vanilla by TileID/WallID name, "v:" vanilla by id (multi-tiles, names not
# reliable), "m:" modded
BRICK, SLAB, MOSS = 'n:GrayBrick', 'n:StoneSlab', 'n:GreenMossBrick'
COPPER, PLATING, GLASS = 'n:CopperBrick', 'n:CopperPlating', 'n:Glass'
PALM, PLATFORM, CHAIN, ROPE = 'n:PalmWood', 'n:Platforms', 'n:Chain', 'v:213'
BEAM, COLUMN, COBWEB = 'v:124', 'n:MarbleColumn', 'v:51'
TORCH = 'm:SoA/Ttorch_tile'
W_BRICK, W_SLAB, W_GLASS = 'n:GrayBrick', 'n:StoneSlab', 'n:Glass'
W_PLANK, W_FENCE, W_CRACK = 'n:Planked', 'n:PalmWoodFence', 'm:SoA/Tidestone_wall_cracked'
TEAL, DEEP_TEAL, GRAY = 6, 18, 27        # PaintID

STONE = {BRICK, SLAB, MOSS}
SOLID = STONE | {COPPER, PLATING, GLASS, PALM}

tile = [[None] * H for _ in range(W)]
fx = [[0] * H for _ in range(W)]
fy = [[0] * H for _ in range(W)]
wall = [[None] * H for _ in range(W)]
slope = [[0] * H for _ in range(W)]
half = [[False] * H for _ in range(W)]
paint = [[0] * H for _ in range(W)]
locked = set()       # openings: later solid fills must not close them
no_slope = set()     # cells whose corners stay square (merlons, frames)
objects = set()      # multi-tile and decor cells: weathering leaves them alone


def inside(x, y): return 0 <= x < W and 0 <= y < H


def put(x, y, key, p=0, force=False):
    if inside(x, y) and (force or (x, y) not in locked):
        tile[x][y] = key
        paint[x][y] = p
        fx[x][y] = fy[x][y] = 0
        half[x][y] = False


def rect(x0, y0, x1, y1, key, p=0):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            put(x, y, key, p)


def wall_rect(x0, y0, x1, y1, key):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            if inside(x, y):
                wall[x][y] = key


def clear(x0, y0, x1, y1, lock=True):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            if inside(x, y):
                tile[x][y] = None
                if lock:
                    locked.add((x, y))


def hollow(x0, y0, x1, y1, shell, key, wall_key):
    """Room: walls of thickness `shell`, back wall inside."""
    rect(x0, y0, x1, y1, key)
    wall_rect(x0 + shell, y0 + shell, x1 - shell, y1 - shell, wall_key)
    clear(x0 + shell, y0 + shell, x1 - shell, y1 - shell)


def arch_opening(x0, x1, top, bottom, ring_key=None):
    """Round-topped opening. ring_key lines the arch with a contrasting ring."""
    cx, r = (x0 + x1) / 2, (x1 - x0 + 1) / 2
    spring = top + r
    tops = {}
    for x in range(x0, x1 + 1):
        dx = abs(x - cx)
        tops[x] = int(round(spring - math.sqrt(max(r * r - dx * dx, 0))))
        clear(x, tops[x], x, bottom)
    if ring_key:
        for x in range(x0 - 1, x1 + 2):
            y_open = tops.get(x, tops[x0] if x < x0 else tops[x1])
            for y in range(y_open - 1, y_open + 1):
                if inside(x, y) and tile[x][y] in STONE:
                    put(x, y, ring_key)
        put(int(round(cx)), tops[int(round(cx))] - 1, COPPER, TEAL)


def obj(x, y, key, frames):
    """frames: list of (dx, dy, frameX, frameY) — multi-tile with exact frames."""
    for dx, dy, fx_, fy_ in frames:
        if inside(x + dx, y + dy):
            tile[x + dx][y + dy] = key
            fx[x + dx][y + dy], fy[x + dx][y + dy] = fx_, fy_
            objects.add((x + dx, y + dy))
            locked.add((x + dx, y + dy))


def grid(w, h, base_x=0, base_y=0):
    return [(dx, dy, base_x + dx * 18, base_y + dy * 18) for dx in range(w) for dy in range(h)]


# Frames taken from sunken_ship.str (verified in game) or vanilla sheet layout
def barrel(x, y): obj(x, y, 'v:21', grid(2, 2, 5 * 36))          # chest style 5 = barrel
def crate(x, y): obj(x, y, 'v:376', grid(2, 2))                  # wooden fishing crate
def table(x, y): obj(x, y, 'v:14', grid(3, 2))
def chair(x, y, facing_right=False): obj(x, y, 'v:15', grid(1, 2, 18 if facing_right else 0))
def bookcase(x, y): obj(x, y, 'v:101', grid(3, 4))
def workbench(x, y): obj(x, y, 'v:18', grid(2, 1))
def cannon(x, y): obj(x, y, 'v:209', grid(4, 3))
def painting(x, y): obj(x, y, 'v:240', grid(3, 3, 756, 54))
def banner(x, y, style=2): obj(x, y, 'v:91', grid(1, 3, style * 18))        # hangs from (x, y-1)
def lantern(x, y): obj(x, y, 'v:42', grid(1, 2))                             # hangs from (x, y-1)


def torch(x, y):
    tile[x][y] = TORCH
    objects.add((x, y))


def cobweb(x, y):
    if inside(x, y) and tile[x][y] is None:
        tile[x][y] = COBWEB
        objects.add((x, y))


def hang(x, y0, y1, key=CHAIN):
    for y in range(y0, y1 + 1):
        if tile[x][y] is None:
            tile[x][y] = key
            objects.add((x, y))


def merlons(x0, x1, y, skip=()):
    """Crenellated parapet: two teeth, one gap."""
    for x in range(x0, x1 + 1):
        if (x - x0) % 3 != 2 and x not in skip:
            put(x, y, SLAB)
            no_slope.add((x, y))


# Smooth 2D value noise for clustered weathering (moss patches, not salt-and-pepper)
_lattice = {}


def _rnd(ix, iy):
    if (ix, iy) not in _lattice:
        _lattice[(ix, iy)] = rng.random()
    return _lattice[(ix, iy)]


def noise(x, y, scale):
    gx, gy = x / scale, y / scale
    ix, iy = int(math.floor(gx)), int(math.floor(gy))
    tx, ty = gx - ix, gy - iy
    sx, sy = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    a = _rnd(ix, iy) + (_rnd(ix + 1, iy) - _rnd(ix, iy)) * sx
    b = _rnd(ix, iy + 1) + (_rnd(ix + 1, iy + 1) - _rnd(ix, iy + 1)) * sx
    return a + (b - a) * sy


# ============================================================================
# Deck and the arcade under it
# ============================================================================
rect(0, DECK, W - 1, DECK, SLAB)                 # paving
rect(0, DECK + 1, W - 1, DECK + 2, BRICK)
rect(0, DECK + 3, W - 1, DECK + 3, SLAB)         # string course right at the waterline
rect(0, DECK + 4, W - 3, H - 1, BRICK)           # arcade body; the pier stops short of the deck end

SPANS = [(6, 16), (22, 30), (36, 44), (50, 57)]
for x0, x1 in SPANS:
    arch_opening(x0, x1, DECK + 5, H - 1, ring_key=SLAB)
arch_opening(64, 71, DECK + 9, H - 1, ring_key=SLAB)      # small arch through the lighthouse pier

# Pier caps / cutwaters: slab bands at the top and a step at the bottom of each pier
for x0, x1 in [(0, 5), (17, 21), (31, 35), (45, 49)]:
    rect(x0, H - 3, x1, H - 3, SLAB)

# ============================================================================
# Collapsed span with a makeshift wooden bridge
# ============================================================================
GAP = (37, 43)
clear(GAP[0], DECK, GAP[1], DECK + 1)
for x, depth in [(36, 0), (44, 0), (37, 1), (43, 1), (38, 2), (42, 2), (39, 2), (40, 3), (41, 2)]:
    clear(x, DECK, x, DECK + depth, lock=False)
for x in range(GAP[0] - 1, GAP[1] + 2):          # planks laid across
    put(x, DECK - 1, PLATFORM, force=True)
    no_slope.add((x, DECK - 1))
for x in [GAP[0], GAP[1]]:                       # props under the planks
    for y in range(DECK, DECK + 3):
        if tile[x][y] is None:
            tile[x][y] = BEAM
            objects.add((x, y))
# fallen merlon block leaning on the deck past the gap
for i, (x, top) in enumerate([(46, DECK - 3), (47, DECK - 4), (48, DECK - 4), (49, DECK - 3)]):
    rect(x, top, x, DECK - 1, SLAB if i % 2 else BRICK)

# ============================================================================
# Left gatehouse (world-edge side)
# ============================================================================
# lower hall
hollow(1, 15, 18, DECK + 1, 2, BRICK, W_BRICK)   # the deck is its floor
rect(0, 15, 19, 15, SLAB)                        # cornice, overhangs on both sides
rect(0, 21, 1, DECK - 1, BRICK)                  # buttress on the edge side
# upper tower, roof gone
hollow(5, 5, 14, 15, 2, BRICK, W_BRICK)
clear(7, 5, 12, 6, lock=False)
for x, top in [(5, 3), (6, 2), (7, 4), (12, 6), (13, 7), (14, 8)]:   # broken crowns
    for y in range(top, 7):
        if (x, y) not in locked:
            put(x, y, BRICK)
wall_rect(7, 3, 11, 6, W_BRICK)                  # the back wall survived higher than the front
for (x, y) in [(10, 3), (11, 3), (11, 4)]:
    wall[x][y] = None
merlons(5, 7, 2)
# floors and openings
for x in range(3, 17):
    put(x, 22, PLATFORM, force=True)                         # hall split into two rooms
    no_slope.add((x, 22))
clear(17, DECK - 4, 18, DECK - 1)                # door to the deck
put(17, DECK - 5, SLAB); put(18, DECK - 5, SLAB)
clear(13, 9, 14, 11)                             # sea-facing window, the cannon looks out of it
clear(1, 17, 2, 19)                              # edge-side window of the hall
for y in range(17, 20):
    wall[1][y] = wall[2][y] = W_BRICK
for x in range(7, 13):                           # tower floor
    put(x, 13, PLATFORM, force=True)
# copper band and banners on the outside
for x in range(1, 19):
    if tile[x][19] in STONE:
        put(x, 19, COPPER, TEAL if rng.random() < 0.5 else 0)
banner(19, 16, 2)
banner(0, 16, 2)
# interior: lower room
table(5, DECK - 2); chair(4, DECK - 2, True); chair(8, DECK - 2)
barrel(12, DECK - 2); crate(14, DECK - 2)
lantern(10, 23)
# upper room
bookcase(3, 18); painting(8, 17); workbench(13, 21)
for x, y in [(15, 17), (16, 17), (16, 18), (12, 16 + 1)]:
    cobweb(x, y)
# tower room
cannon(9, 10)
for x, y in [(7, 7), (8, 7), (7, 8), (12, 7)]:
    cobweb(x, y)

# ============================================================================
# Deck life
# ============================================================================
# Parapet as a back wall: gives the deck depth and leaves the deck free for props
for x in list(range(19, 36)) + list(range(46, 60)):
    wall[x][DECK - 1] = W_SLAB
    if (x - 19) % 3 != 2:
        wall[x][DECK - 2] = W_SLAB
# lamp posts: copper post, bracket, hanging lantern
for x in [22, 52]:
    rect(x, DECK - 6, x, DECK - 1, COPPER)
    paint[x][DECK - 6] = TEAL
    no_slope.update((x, y) for y in range(DECK - 6, DECK))
    put(x + 1, DECK - 6, PLATING, TEAL)
    no_slope.add((x + 1, DECK - 6))
    lantern(x + 1, DECK - 5)
cannon(29, DECK - 3)                             # facing the open sea
put(28, DECK - 1, SLAB); put(33, DECK - 1, SLAB)
crate(24, DECK - 2); barrel(26, DECK - 2)
crate(54, DECK - 2); barrel(56, DECK - 2); crate(55, DECK - 4)
for x in [34, 59]:                               # mooring bollards
    put(x, DECK - 1, PLATING, TEAL)
    half[x][DECK - 1] = True
    no_slope.add((x, DECK - 1))

# ============================================================================
# Lighthouse (sea side)
# ============================================================================
TX0, TX1 = 61, 76
rect(TX0, 15, TX1, DECK - 1, BRICK)                      # lower shaft, walls 3 thick
rect(TX0 + 1, 8, TX1 - 1, 14, BRICK)                     # upper shaft, a step narrower
wall_rect(TX0 + 3, 10, TX1 - 3, DECK - 1, W_SLAB)
clear(TX0 + 3, 10, TX1 - 3, DECK - 1)
for y in [16, 22]:                                       # floors
    for x in range(TX0 + 3, TX1 - 2):
        put(x, y, PLATFORM, force=True); no_slope.add((x, y))
for y in [15, 22]:                                       # copper bands
    for x in range(TX0 - 1, TX1 + 2):
        if inside(x, y) and tile[x][y] in STONE:
            put(x, y, COPPER, TEAL if (x + y) % 2 else 0)
rect(TX0 - 1, 14, TX1 + 1, 14, SLAB)                     # ledge at the step
# door with marble columns
clear(TX0, DECK - 4, TX0 + 2, DECK - 1)
for x in range(TX0, TX0 + 3):
    put(x, DECK - 5, SLAB)
for y in range(DECK - 5, DECK):
    put(TX0 - 1, y, COLUMN); objects.add((TX0 - 1, y))
# windows with glass
for x0, x1, y0 in [(TX1 - 2, TX1, 18), (TX0, TX0 + 2, 18), (TX1 - 2, TX1 - 1, 11), (TX0 + 1, TX0 + 2, 11)]:
    for x in range(x0, x1 + 1):
        for y in range(y0, y0 + 2):
            tile[x][y] = None
            wall[x][y] = W_GLASS
            locked.add((x, y))
# interior
table(67, DECK - 2); chair(66, DECK - 2, True); barrel(71, DECK - 2)
lantern(69, 23)
bookcase(65, 18); painting(69, 18); cobweb(TX1 - 3, 17); cobweb(TX1 - 3, 18); cobweb(TX1 - 4, 17)
workbench(66, 15); lantern(68, 17)
hang(71, 10, 15)
cobweb(TX0 + 3, 10); cobweb(TX0 + 4, 10); cobweb(TX0 + 3, 11)
# gallery with railing
rect(TX0 - 2, 7, TX1 + 2, 7, SLAB)
for x in list(range(TX0 - 2, TX0 + 2)) + list(range(TX1 - 1, TX1 + 3)):
    wall[x][6] = W_FENCE
# lantern room: copper frame, glass, the light
rect(65, 2, 72, 2, COPPER, TEAL)
for y in range(3, 7):
    put(65, y, GLASS); put(72, y, GLASS)
wall_rect(66, 3, 71, 6, W_GLASS)
torch(67, 6); torch(68, 6); torch(69, 6); torch(70, 6)
rect(66, 1, 71, 1, PLATING, TEAL)                        # roof, stepped
rect(67, 0, 70, 0, PLATING, TEAL)
# crane on the land side: arm, brace, rope down to the deck
for x in range(52, TX0):
    put(x, 16, PALM); no_slope.add((x, 16))
for i in range(4):
    put(TX0 - 1 - i, 17 + i, PALM)
hang(53, 17, DECK - 6, ROPE)
# chain from the pier into the sea
hang(W - 2, DECK + 4, DECK + 12)

# ============================================================================
# Weathering
# ============================================================================
for x in range(W):
    for y in range(H):
        if tile[x][y] not in (BRICK, SLAB) or (x, y) in objects:
            continue
        if y >= WATERLINE:
            depth = (y - WATERLINE) / (H - 1 - WATERLINE)
            n = noise(x, y, 5.0)
            if n < 0.25 + 0.35 * depth:              # moss creeps up from the bottom in patches
                put(x, y, MOSS, force=True)
            elif n > 0.8:
                paint[x][y] = DEEP_TEAL              # algae film
        elif noise(x + 100, y, 4.0) > 0.78:
            paint[x][y] = GRAY                       # sun-bleached and soot-darkened streaks


def empty(x, y): return not inside(x, y) or tile[x][y] is None or tile[x][y] not in SOLID


for x in range(W):
    for y in range(H):
        if tile[x][y] not in SOLID or (x, y) in no_slope or half[x][y] or tile[x][y] == GLASS:
            continue
        left, right, up, down = empty(x - 1, y), empty(x + 1, y), empty(x, y - 1), empty(x, y + 1)
        if left and right:
            continue
        if down and not up and y < H - 1 and y not in (DECK, DECK + 3):
            if right: slope[x][y] = 4                # ◤ arch / overhang corner
            elif left: slope[x][y] = 3               # ◥
        elif up and not down and y not in (DECK,) and tile[x][y] not in (COPPER, PLATING):
            if right: slope[x][y] = 2                # ◣ broken top
            elif left: slope[x][y] = 1               # ◢

# ============================================================================
# Export
# ============================================================================
tile_legend, wall_legend = [], []


def legend(lst, key):
    if key not in lst:
        lst.append(key)
    return lst.index(key)


t, ofx, ofy, wl, lq, sl, pt, mask = [], [], [], [], [], [], [], bytearray()
for x in range(W):
    for y in range(H):
        key = tile[x][y]
        t.append(legend(tile_legend, key) if key else -1)
        ofx.append(fx[x][y] if key else 0)
        ofy.append(fy[x][y] if key else 0)
        wl.append(legend(wall_legend, wall[x][y]) if wall[x][y] else -1)
        lq.append(0)
        sl.append((slope[x][y] | (8 if half[x][y] else 0)) if key else 0)
        pt.append(paint[x][y] if key else 0)
        # Only the masonry belongs to the structure: rock and water in the arches stay
        mask.append(1 if key or wall[x][y] else 0)

root = ('C', {
    'version': ('i', 3), 'w': ('i', W), 'h': ('i', H), 'filters': ('i', 0),
    'tileLegend': ('L', tagio.STRING, [('S', k) for k in tile_legend]),
    'wallLegend': ('L', tagio.STRING, [('S', k) for k in wall_legend]),
    't': ('I', t), 'fx': ('I', ofx), 'fy': ('I', ofy), 'wl': ('I', wl), 'lq': ('I', lq),
    'sl': ('I', sl), 'pt': ('I', pt), 'mask': ('B', bytes(mask)),
})
tagio.write(sys.argv[1], root)

glyph = {BRICK: '#', SLAB: '=', MOSS: '%', COPPER: 'c', PLATING: 'C', GLASS: 'g', PALM: 'w',
         PLATFORM: '-', CHAIN: '|', ROPE: '!', BEAM: 'I', COLUMN: 'H', COBWEB: 'x', TORCH: '*',
         'v:21': 'B', 'v:376': 'K', 'v:14': 'T', 'v:15': 'h', 'v:101': 'L', 'v:18': 'b', 'v:209': 'O',
         'v:240': 'P', 'v:91': 'F', 'v:42': 'o'}
for y in range(H):
    row = ''.join(glyph.get(tile[x][y], '.' if wall[x][y] else ' ') for x in range(W))
    print('%2d %s%s' % (y, row, '  ~ water' if y == WATERLINE else ''))
print('legend', tile_legend, wall_legend)
