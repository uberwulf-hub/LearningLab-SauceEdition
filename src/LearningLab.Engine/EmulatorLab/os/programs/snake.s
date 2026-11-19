# snake.s - a tiny snake game. Steer with the arrow keys (or W A S D), eat the red apples, don't hit the walls
# or yourself. Esc stops.   Try it with:  run snake      or let the computer play:  run snake demo
#
# The board is 32 x 19 cells of 10 x 10 pixels (the top line is for the score). The snake's cells are kept in a
# "ring buffer": the head is added at one end and the tail removed at the other, so nothing needs moving.
# A byte per cell (the grid) says if the snake is there, so checking for a crash is quick.

.include "_syscalls.s"

.equ W, 32                  # cells across
.equ H, 20                  # rows (row 0 is the score line)
.equ CELL, 10
.equ RING, 1024             # room for the snake's cells (more than the board has)
.equ C_BG1, 22              # colours: two dark greens for the board (from the palette's colour cube)
.equ C_BG2, 28
.equ C_BODY, 10
.equ C_HEAD, 14
.equ C_APPLE, 12

.text
_start:
    # "run snake demo": the computer plays
    lbu  t0, 0(a0)
    li   t1, 'd'
    bne  t0, t1, 1f
    li   t0, 1
    la   t1, demo
    sw   t0, 0(t1)
1:  li   a0, 2              # graphics with text on top
    li   a7, SYS_SCREEN_MODE
    ecall

new_game:
    li   a7, SYS_CLEAR_TEXT
    ecall
    call draw_board
    la   a0, grid           # empty grid
    li   t0, W * H
    add  t1, a0, t0
1:  sb   zero, 0(a0)
    addi a0, a0, 1
    bltu a0, t1, 1b
    la   t0, ring_head      # a snake of 3 cells in the middle, heading right
    sw   zero, 0(t0)
    la   t0, length
    sw   zero, 0(t0)
    li   s1, 10             # s0, s1 = head x, y
    li   s2, 1              # s2 = dx
    li   s3, 0              # s3 = dy
    li   s0, 6
    li   a0, 6
    li   a1, 10
    call add_head
    li   a0, 7
    li   a1, 10
    call add_head
    li   s0, 7
    li   a0, 8
    li   a1, 10
    call add_head
    li   s0, 8
    la   t0, score
    sw   zero, 0(t0)
    li   s4, 120            # s4 = milliseconds per step (it gets faster)
    call place_apple
    call show_score

step:
    call read_keys          # may change s2, s3 (direction); a0 = 1 to quit
    bnez a0, quit
    la   t0, demo
    lw   t0, 0(t0)
    beqz t0, 1f
    call autopilot
1:  add  s5, s0, s2         # s5, s6 = the new head position
    add  s6, s1, s3
    mv   a0, s5
    mv   a1, s6
    call is_free
    beqz a0, game_over
    la   t0, apple_x        # an apple?
    lw   t0, 0(t0)
    la   t1, apple_y
    lw   t1, 0(t1)
    bne  t0, s5, 2f
    bne  t1, s6, 2f
    la   t0, score          # yes: grow (don't remove the tail), score, a new apple, a bit faster
    lw   t1, 0(t0)
    addi t1, t1, 1
    sw   t1, 0(t0)
    li   t0, 50
    ble  s4, t0, 3f
    addi s4, s4, -4
3:  mv   a0, s5
    mv   a1, s6
    call add_head
    call place_apple
    call show_score
    j    4f
2:  call remove_tail
    mv   a0, s5
    mv   a1, s6
    call add_head
4:  mv   s0, s5
    mv   s1, s6
    mv   a0, s4
    li   a7, SYS_SLEEP
    ecall
    j    step

game_over:
    la   t0, best           # a new best score?
    lw   t1, 0(t0)
    la   t2, score
    lw   t2, 0(t2)
    bge  t1, t2, 1f
    sw   t2, 0(t0)
1:  la   a0, msg_over
    li   a7, SYS_PRINT
    ecall
    la   t0, score
    lw   a0, 0(t0)
    li   a7, SYS_PRINT_NUM
    ecall
    la   t0, demo
    lw   t0, 0(t0)
    beqz t0, 2f
    li   a0, 1500           # demo: a short pause, then again
    li   a7, SYS_SLEEP
    ecall
    li   a7, SYS_POLLKEY
    ecall
    srli a0, a0, 8
    li   t0, SCAN_ESC
    beq  a0, t0, quit
    j    new_game
2:  la   a0, msg_again
    li   a7, SYS_PRINT
    ecall
3:  li   a7, SYS_GETKEY
    ecall
    beqz a0, quit           # Ctrl+C
    srli t1, a0, 8
    li   t0, SCAN_ESC
    beq  t1, t0, quit
    andi a0, a0, 0xff
    li   t0, 13
    beq  a0, t0, new_game
    li   t0, ' '
    beq  a0, t0, new_game
    j    3b
quit:
    li   a0, 0
    li   a7, SYS_EXIT
    ecall

# read_keys: take every waiting key; arrows / WASD turn (never straight back); returns a0 = 1 for Esc
read_keys:
1:  li   a7, SYS_POLLKEY
    ecall
    beqz a0, 9f
    srli t1, a0, 8          # scancode
    andi t2, a0, 0xff       # ASCII
    ori  t2, t2, 0x20       # lower case letters
    li   t0, SCAN_ESC
    beq  t1, t0, 8f
    li   t0, SCAN_UP
    beq  t1, t0, up
    li   t0, 'w'
    beq  t2, t0, up
    li   t0, SCAN_DOWN
    beq  t1, t0, down
    li   t0, 's'
    beq  t2, t0, down
    li   t0, SCAN_LEFT
    beq  t1, t0, left
    li   t0, 'a'
    beq  t2, t0, left
    li   t0, SCAN_RIGHT
    beq  t1, t0, right
    li   t0, 'd'
    beq  t2, t0, right
    j    1b
up:     bnez s3, 1b         # moving up or down already: ignore
        li   s2, 0
        li   s3, -1
        j    1b
down:   bnez s3, 1b
        li   s2, 0
        li   s3, 1
        j    1b
left:   bnez s2, 1b
        li   s2, -1
        li   s3, 0
        j    1b
right:  bnez s2, 1b
        li   s2, 1
        li   s3, 0
        j    1b
8:  li   a0, 1
    ret
9:  li   a0, 0
    ret

# autopilot: the computer's move. Try, in order: towards the apple across, towards it up/down, straight on,
# the other ways - and take the first that doesn't crash.
autopilot:
    addi sp, sp, -16
    sw   ra, 12(sp)
    la   t0, apple_x
    lw   t0, 0(t0)
    sub  t0, t0, s0         # apple x - head x
    la   t1, apple_y
    lw   t1, 0(t1)
    sub  t1, t1, s1
    # choice 1: across
    beqz t0, 1f
    sltz t2, t0             # t2 = 1 if the apple is to the left
    li   a0, 1
    beqz t2, 2f
    li   a0, -1
2:  li   a1, 0
    sw   t1, 4(sp)
    call try_dir
    bnez a0, 9f
    lw   t1, 4(sp)
1:  # choice 2: up / down
    beqz t1, 3f
    li   a1, 1
    bgtz t1, 4f
    li   a1, -1
4:  li   a0, 0
    call try_dir
    bnez a0, 9f
3:  mv   a0, s2             # choice 3: straight on
    mv   a1, s3
    call try_dir
    bnez a0, 9f
    li   a0, 1              # then any way at all
    li   a1, 0
    call try_dir
    bnez a0, 9f
    li   a0, -1
    li   a1, 0
    call try_dir
    bnez a0, 9f
    li   a0, 0
    li   a1, 1
    call try_dir
    bnez a0, 9f
    li   a0, 0
    li   a1, -1
    call try_dir
9:  lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# try_dir(a0 = dx, a1 = dy) -> a0 = 1 and the direction is taken if it is safe (and not straight back)
try_dir:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   a0, 8(sp)
    sw   a1, 4(sp)
    add  t0, a0, s2         # straight back would be dx = -s2 and dy = -s3
    add  t1, a1, s3
    or   t0, t0, t1
    beqz t0, 1f
    add  a0, s0, a0
    add  a1, s1, a1
    call is_free
    beqz a0, 1f
    lw   s2, 8(sp)
    lw   s3, 4(sp)
    li   a0, 1
    j    2f
1:  li   a0, 0
2:  lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# is_free(a0 = x, a1 = y) -> a0 = 1 if the cell is on the board and not snake
is_free:
    li   t0, W
    bgeu a0, t0, 1f
    addi t1, a1, -1         # rows 1 .. H-1
    li   t0, H - 1
    bgeu t1, t0, 1f
    li   t0, W
    mul  t1, a1, t0
    add  t1, t1, a0
    la   t0, grid
    add  t1, t1, t0
    lbu  t2, 0(t1)
    seqz a0, t2
    ret
1:  li   a0, 0
    ret

# add_head(a0 = x, a1 = y): the snake's new head (the old head becomes body colour)
add_head:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   a0, 8(sp)
    sw   a1, 4(sp)
    la   t0, length
    lw   t1, 0(t0)
    beqz t1, 1f
    mv   a0, s0             # repaint the old head as body
    mv   a1, s1
    li   a2, C_BODY
    call draw_cell
1:  lw   a0, 8(sp)
    lw   a1, 4(sp)
    li   t0, W              # mark the grid
    mul  t1, a1, t0
    add  t1, t1, a0
    la   t0, grid
    add  t1, t1, t0
    li   t2, 1
    sb   t2, 0(t1)
    la   t0, ring_head      # store x and y at the head of the ring
    lw   t1, 0(t0)
    slli t2, t1, 1
    la   t3, ring
    add  t2, t2, t3
    sb   a0, 0(t2)
    sb   a1, 1(t2)
    addi t1, t1, 1
    andi t1, t1, RING - 1
    sw   t1, 0(t0)
    la   t0, length
    lw   t1, 0(t0)
    addi t1, t1, 1
    sw   t1, 0(t0)
    li   a2, C_HEAD
    call draw_cell
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# remove_tail: the oldest cell of the snake goes
remove_tail:
    addi sp, sp, -16
    sw   ra, 12(sp)
    la   t0, ring_head
    lw   t1, 0(t0)
    la   t0, length
    lw   t2, 0(t0)
    sub  t1, t1, t2         # tail = head - length (wrapping around the ring)
    andi t1, t1, RING - 1
    addi t2, t2, -1
    sw   t2, 0(t0)
    slli t1, t1, 1
    la   t3, ring
    add  t1, t1, t3
    lbu  a0, 0(t1)
    lbu  a1, 1(t1)
    li   t0, W
    mul  t1, a1, t0
    add  t1, t1, a0
    la   t0, grid
    add  t1, t1, t0
    sb   zero, 0(t1)
    call draw_empty
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# place_apple: a random free cell
place_apple:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
1:  li   a7, SYS_RANDOM
    ecall
    li   t0, W
    remu s0, a0, t0
    srli a0, a0, 8
    li   t0, H - 1
    remu s1, a0, t0
    addi s1, s1, 1
    mv   a0, s0
    mv   a1, s1
    call is_free
    beqz a0, 1b
    la   t0, apple_x
    sw   s0, 0(t0)
    la   t0, apple_y
    sw   s1, 0(t0)
    mv   a0, s0
    mv   a1, s1
    li   a2, C_APPLE
    call draw_cell
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# draw_cell(a0 = x, a1 = y, a2 = colour): an 8 x 8 square inside the cell
draw_cell:
    mv   a4, a2
    li   t0, CELL
    mul  a0, a0, t0
    addi a0, a0, 1
    mul  a1, a1, t0
    addi a1, a1, 1
    li   a2, CELL - 2
    li   a3, CELL - 2
    li   a7, SYS_RECT
    ecall
    ret

# draw_empty(a0 = x, a1 = y): the board's chequered background for that cell
draw_empty:
    add  t0, a0, a1
    andi t0, t0, 1
    li   a4, C_BG1
    beqz t0, 1f
    li   a4, C_BG2
1:  li   t0, CELL
    mul  a0, a0, t0
    mul  a1, a1, t0
    li   a2, CELL
    li   a3, CELL
    li   a7, SYS_RECT
    ecall
    ret

# draw_board: a black score line and the chequered board
draw_board:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    li   a0, 0
    li   a1, 0
    li   a2, 320
    li   a3, CELL
    li   a4, 0
    li   a7, SYS_RECT
    ecall
    li   s1, 1
1:  li   s0, 0
2:  mv   a0, s0
    mv   a1, s1
    call draw_empty
    addi s0, s0, 1
    li   t0, W
    blt  s0, t0, 2b
    addi s1, s1, 1
    li   t0, H
    blt  s1, t0, 1b
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# show_score: the top line of text
show_score:
    la   a0, msg_score
    li   a7, SYS_PRINT
    ecall
    la   t0, score
    lw   a0, 0(t0)
    li   a7, SYS_PRINT_NUM
    ecall
    la   a0, msg_best
    li   a7, SYS_PRINT
    ecall
    la   t0, best
    lw   a0, 0(t0)
    li   a7, SYS_PRINT_NUM
    ecall
    la   a0, msg_keys
    li   a7, SYS_PRINT
    ecall
    ret

.data
.align 2
demo:      .word 0
score:     .word 0
best:      .word 0
length:    .word 0
ring_head: .word 0
apple_x:   .word 0
apple_y:   .word 0
ring:      .space RING * 2      # x, y bytes for each cell of the snake
grid:      .space W * H
msg_score: .asciz "\e[H\e[93m SNAKE \e[97m  Score: "
msg_best:  .asciz "   Best: "
msg_keys:  .asciz "\e[37m   (arrows or WASD, Esc stops)\e[K\e[0m"
msg_over:  .asciz "\e[H\e[K\e[91m GAME OVER! \e[97mScore: "
msg_again: .asciz "\e[37m   Enter: play again, Esc: stop\e[0m"
