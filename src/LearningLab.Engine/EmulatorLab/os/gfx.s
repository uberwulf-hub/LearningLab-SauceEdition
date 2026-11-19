# gfx.s - drawing on the screen.
#
# The screen is 320 x 200 pixels. Each pixel is one byte in the framebuffer at FB_BASE: the pixel at (x, y) is
# at FB_BASE + y * 320 + x. The byte is a colour number (0-255) and the palette says which real colour each
# number shows (0x00RRGGBB). Colours 0-15 are the classic PC colours; the text console uses them too.

.text
# gfx_mode(a0 = 0 text, 1 graphics, 2 graphics with text on top)
gfx_mode:
    li   t0, VIDEO_BASE
    sw   a0, VIDEO_MODE(t0)
    li   a0, 0
    ret

# gfx_clear(a0 = colour): fill the whole screen, four pixels (one word) at a time
gfx_clear:
    andi a0, a0, 0xff
    slli t0, a0, 8                  # the colour byte copied into all four bytes of a word
    or   a0, a0, t0
    slli t0, a0, 16
    or   a0, a0, t0
    li   t0, FB_BASE
    li   t1, FB_BASE + SCREEN_W * SCREEN_H
1:  sw   a0, 0(t0)
    addi t0, t0, 4
    bltu t0, t1, 1b
    li   a0, 0
    ret

# gfx_pixel(a0 = x, a1 = y, a2 = colour). Pixels off the screen are ignored ("clipping").
gfx_pixel:
    li   t0, SCREEN_W
    bgeu a0, t0, 1f                 # unsigned: a negative x is a huge number, so this catches x < 0 too
    li   t1, SCREEN_H
    bgeu a1, t1, 1f
    mul  t1, a1, t0                 # y * 320
    add  t1, t1, a0                 # + x
    li   t0, FB_BASE
    add  t1, t1, t0
    sb   a2, 0(t1)
1:  li   a0, 0
    ret

# gfx_get_pixel(a0 = x, a1 = y) -> a0 = colour (0 off the screen)
gfx_get_pixel:
    li   t0, SCREEN_W
    bgeu a0, t0, 1f
    li   t1, SCREEN_H
    bgeu a1, t1, 1f
    mul  t1, a1, t0
    add  t1, t1, a0
    li   t0, FB_BASE
    add  t1, t1, t0
    lbu  a0, 0(t1)
    ret
1:  li   a0, 0
    ret

# gfx_rect(a0 = x, a1 = y, a2 = width, a3 = height, a4 = colour): a filled rectangle, clipped to the screen
gfx_rect:
    add  a2, a2, a0                 # a2 = right edge (exclusive)
    add  a3, a3, a1                 # a3 = bottom edge (exclusive)
    bgez a0, 1f                     # clip each side
    li   a0, 0
1:  bgez a1, 2f
    li   a1, 0
2:  li   t0, SCREEN_W
    ble  a2, t0, 3f
    mv   a2, t0
3:  li   t0, SCREEN_H
    ble  a3, t0, 4f
    mv   a3, t0
4:  bge  a0, a2, 7f                 # nothing left to draw
    bge  a1, a3, 7f
    li   t0, SCREEN_W
    mul  t1, a1, t0
    li   t2, FB_BASE
    add  t1, t1, t2                 # t1 = start of row y
5:  add  t3, t1, a0                 # first pixel of this row
    add  t4, t1, a2                 # one past the last
6:  sb   a4, 0(t3)
    addi t3, t3, 1
    bltu t3, t4, 6b
    addi t1, t1, SCREEN_W
    addi a1, a1, 1
    blt  a1, a3, 5b
7:  li   a0, 0
    ret

# gfx_palette(a0 = colour number 0-255, a1 = 0x00RRGGBB)
gfx_palette:
    andi a0, a0, 0xff
    slli a0, a0, 2
    li   t0, VIDEO_BASE + VIDEO_PALETTE
    add  t0, t0, a0
    sw   a1, 0(t0)
    li   a0, 0
    ret
