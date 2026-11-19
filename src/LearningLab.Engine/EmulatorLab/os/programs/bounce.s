# bounce.s - bouncing shapes. Shows graphics mode, the palette, and a simple animation loop:
#   erase every shape, move it, draw it again, wait a moment, repeat.
# Press any key to stop.   Try it with:  run bounce

.include "_syscalls.s"

.equ COUNT, 8               # how many shapes
.equ SHAPE, 24              # bytes per shape: x, y, dx, dy, size, colour (6 words)
.equ BG,    200             # colours 200-249: the background gradient (we set them up ourselves)

.text
_start:
    # 1. Make 50 new colours, from deep blue to purple, by changing the palette
    li   s0, 0
1:  li   t0, 50
    bge  s0, t0, 2f
    addi a0, s0, BG         # colour number
    slli t1, s0, 1          # red grows from 0 to 98...
    slli t1, t1, 16
    srli t2, s0, 1          # ...green a little...
    slli t2, t2, 8
    or   t1, t1, t2
    li   t3, 3
    mul  t3, s0, t3
    addi t3, t3, 40         # ...blue from 40 to 187
    or   a1, t1, t3         # a1 = 0x00RRGGBB
    li   a7, SYS_PALETTE
    ecall
    addi s0, s0, 1
    j    1b

    # 2. Graphics with the text on top, and a title
2:  li   a0, 2
    li   a7, SYS_SCREEN_MODE
    ecall
    li   a7, SYS_CLEAR_TEXT
    ecall
    la   a0, title
    li   a7, SYS_PRINT
    ecall

    # 3. Paint the background: one line per row, a slightly different colour every 4 rows
    li   s0, 0
3:  li   t0, 200
    bge  s0, t0, frame
    mv   a0, s0
    call background_row
    addi s0, s0, 1
    j    3b

    # 4. The animation
frame:
    li   s0, 0              # each shape in turn: erase it, move it, draw it again (one at a time, so a shape is
5:  mv   a0, s0             # only missing from the screen for a moment and nothing flickers)
    call shape_address
    mv   s1, a0
    call erase
    mv   a0, s1
    call move
    mv   a0, s1
    andi a1, s0, 1          # even shapes are squares, odd ones are balls
    call draw
    addi s0, s0, 1
    li   t0, COUNT
    blt  s0, t0, 5b
    li   a0, 20             # wait 20 ms: about 50 pictures a second
    li   a7, SYS_SLEEP
    ecall
    li   a7, SYS_POLLKEY    # a key stops the show
    ecall
    beqz a0, frame
    li   a0, 0
    li   a7, SYS_EXIT
    ecall

# shape_address(a0 = number) -> a0 = address of that shape's data
shape_address:
    li   t0, SHAPE
    mul  a0, a0, t0
    la   t0, shapes
    add  a0, a0, t0
    ret

# background_row(a0 = y): draw row y of the background
background_row:
    li   t0, 200
    bgeu a0, t0, 1f         # off the screen
    mv   a1, a0             # y
    srli a4, a0, 2
    addi a4, a4, BG         # colour = BG + y / 4
    li   a0, 0              # x
    li   a2, 320            # width
    li   a3, 1              # height
    li   a7, SYS_RECT
    ecall
1:  ret

# erase(a0 = shape): paint the background back where the shape is
erase:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    sw   s2, 0(sp)
    mv   s2, a0
    lw   s0, 4(s2)          # y
    lw   t0, 16(s2)
    add  s1, s0, t0
    addi s1, s1, 1          # y + size + 1 (a ball is one pixel wider than its size)
1:  bge  s0, s1, 2f
    li   t0, 200
    bgeu s0, t0, 3f
    lw   a0, 0(s2)          # x
    mv   a1, s0
    lw   a2, 16(s2)
    addi a2, a2, 1          # width = size + 1
    li   a3, 1
    srli a4, s0, 2
    addi a4, a4, BG
    li   a7, SYS_RECT
    ecall
3:  addi s0, s0, 1
    j    1b
2:  lw   s2, 0(sp)
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# move(a0 = shape): x += dx, y += dy, bouncing off the edges (and changing colour when it does)
move:
    lw   t0, 0(a0)          # x
    lw   t1, 8(a0)          # dx
    lw   t2, 16(a0)         # size
    add  t0, t0, t1
    bgez t0, 1f             # past the left edge?
    li   t0, 0
    neg  t1, t1
    j    2f
1:  li   t3, 320
    sub  t3, t3, t2         # the furthest right x
    ble  t0, t3, 3f
    mv   t0, t3
    neg  t1, t1
2:  lw   t4, 20(a0)         # bounced: next colour (9-15)
    addi t4, t4, 1
    li   t5, 16
    blt  t4, t5, 4f
    li   t4, 9
4:  sw   t4, 20(a0)
3:  sw   t0, 0(a0)
    sw   t1, 8(a0)
    lw   t0, 4(a0)          # the same for y
    lw   t1, 12(a0)
    add  t0, t0, t1
    bgez t0, 5f
    li   t0, 0
    neg  t1, t1
    j    6f
5:  li   t3, 200
    sub  t3, t3, t2
    ble  t0, t3, 6f
    mv   t0, t3
    neg  t1, t1
6:  sw   t0, 4(a0)
    sw   t1, 12(a0)
    ret

# draw(a0 = shape, a1 = 0 square, 1 ball)
draw:
    addi sp, sp, -32
    sw   ra, 28(sp)
    sw   s0, 24(sp)
    sw   s1, 20(sp)
    sw   s2, 16(sp)
    sw   s3, 12(sp)
    sw   s4, 8(sp)
    mv   s0, a0
    bnez a1, ball
    lw   a0, 0(s0)          # a square: one rectangle
    lw   a1, 4(s0)
    lw   a2, 16(s0)
    mv   a3, a2
    lw   a4, 20(s0)
    li   a7, SYS_RECT
    ecall
    j    9f
ball:
    # a ball: for each row dy from -r to r, the widest w with w*w + dy*dy <= r*r, then one line 2w+1 long
    lw   s1, 16(s0)
    srli s1, s1, 1          # s1 = r
    lw   s3, 0(s0)
    add  s3, s3, s1         # s3 = centre x
    lw   s4, 4(s0)
    add  s4, s4, s1         # s4 = centre y
    neg  s2, s1             # s2 = dy
1:  bgt  s2, s1, 9f
    mv   t0, s1             # w = r, then shrink until it fits
    mul  t2, s1, s1         # r*r (+ r: a little extra makes rounder edges on a pixel grid)
    add  t2, t2, s1
    mul  t3, s2, s2         # dy*dy
2:  mul  t1, t0, t0
    add  t1, t1, t3
    ble  t1, t2, 3f
    addi t0, t0, -1
    j    2b
3:  sub  a0, s3, t0         # x = centre - w
    add  a1, s4, s2         # y = centre + dy
    slli a2, t0, 1
    addi a2, a2, 1          # width 2w + 1
    li   a3, 1
    lw   a4, 20(s0)
    li   a7, SYS_RECT
    ecall
    addi s2, s2, 1
    j    1b
9:  lw   s4, 8(sp)
    lw   s3, 12(sp)
    lw   s2, 16(sp)
    lw   s1, 20(sp)
    lw   s0, 24(sp)
    lw   ra, 28(sp)
    addi sp, sp, 32
    ret

.data
title: .asciz "\e[93m Bouncing shapes \e[0m - press any key to stop\n"
.align 2
shapes:     #   x,   y,  dx,  dy, size, colour
    .word      10,  20,   3,   2,   24,  9
    .word     100,  40,  -2,   3,   30,  10
    .word     200,  60,   4,  -2,   16,  11
    .word      50, 120,  -3,  -3,   40,  12
    .word     250, 150,   2,   4,   20,  13
    .word     160, 100,  -4,   1,   28,  14
    .word      20, 170,   1,  -4,   12,  15
    .word     280,  30,  -1,   2,   36,  9
