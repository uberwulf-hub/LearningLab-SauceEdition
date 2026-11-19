# keyboard.s - keys and the line editor.
#
# When a key is pressed, the keyboard raises an interrupt. The trap handler then calls kbd_poll, which moves the
# key from the keyboard into LabOS's own buffer (a "ring buffer" of 64 keys). Programs and the shell take keys
# out of that buffer with getkey.
#
# Each key is a word: bits 0-7 = its ASCII code (0 for keys like the arrows), bits 8-15 = its PC scancode.

.equ KEY_UP,     0x48               # scancodes of the arrow keys
.equ KEY_DOWN,   0x50
.equ KEY_LEFT,   0x4B
.equ KEY_RIGHT,  0x4D
.equ KEYBUF_SIZE, 64
.equ HISTORY_LINES, 8
.equ HISTORY_WIDTH, 80

.text
# kbd_poll: move every waiting key from the keyboard into the buffer. Ctrl+C (ASCII 3) while a program runs
# asks LabOS to stop the program instead.
kbd_poll:
    li   t0, KBD_BASE
1:  lw   t1, KBD_STATUS(t0)
    beqz t1, 3f
    lw   t1, KBD_DATA(t0)           # reading DATA takes the key from the keyboard
    andi t2, t1, 0xff
    li   t3, 3
    bne  t2, t3, 2f
    la   t3, program_running        # Ctrl+C
    lw   t3, 0(t3)
    beqz t3, 2f
    li   t3, 1
    la   t4, break_requested
    sw   t3, 0(t4)
    j    1b
2:  la   t2, keybuf_head            # store at head, if there is room
    lw   t3, 0(t2)
    addi t4, t3, 1
    andi t4, t4, KEYBUF_SIZE - 1
    la   t5, keybuf_tail
    lw   t5, 0(t5)
    beq  t4, t5, 1b                 # full: drop the key
    la   t5, keybuf
    slli t6, t3, 2
    add  t5, t5, t6
    sw   t1, 0(t5)
    sw   t4, 0(t2)
    j    1b
3:  ret

# key_pop -> a0 = the oldest key in the buffer, or 0 if it is empty
key_pop:
    la   t0, keybuf_tail
    lw   t1, 0(t0)
    la   t2, keybuf_head
    lw   t2, 0(t2)
    li   a0, 0
    beq  t1, t2, 1f
    la   t3, keybuf
    slli t4, t1, 2
    add  t3, t3, t4
    lw   a0, 0(t3)
    addi t1, t1, 1
    andi t1, t1, KEYBUF_SIZE - 1
    sw   t1, 0(t0)
1:  ret

# pollkey -> a0 = a key, or 0 if none (doesn't wait)
pollkey:
    addi sp, sp, -16
    sw   ra, 12(sp)
    csrrci t0, mstatus, 8           # interrupts off while we look at the buffer
    sw   t0, 8(sp)
    call kbd_poll
    call key_pop
    lw   t0, 8(sp)
    andi t0, t0, 8
    csrs mstatus, t0                # interrupts back on if they were on
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# getkey -> a0 = the next key, waiting for one if needed. Returns 0 if a program was asked to stop (Ctrl+C).
#
# Waiting the right way: turn interrupts off, look in the buffer, and only if it is empty run wfi ("wait for
# interrupt"). The processor then rests until a key (or the timer) needs attention. Turning interrupts off first
# matters: if a key arrived between looking and wfi, we would sleep with a key waiting. wfi wakes up for a pending
# interrupt even while interrupts are off; we then turn them on again and the interrupt is handled.
getkey:
    addi sp, sp, -16
    sw   ra, 12(sp)
1:  csrrci t0, mstatus, 8
    sw   t0, 8(sp)
    call kbd_poll
    call key_pop
    bnez a0, 2f
    la   t0, break_requested
    lw   t0, 0(t0)
    bnez t0, 2f
key_wait_wfi:
    wfi
    call timer_poll                 # keep the clock going (inside a system call, interrupts stay off)
    lw   t0, 8(sp)
    andi t0, t0, 8
    csrs mstatus, t0                # if interrupts were on, any waiting interrupt is handled right here
    j    1b
2:  lw   t0, 8(sp)
    andi t0, t0, 8
    csrs mstatus, t0
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# readline(a0 = buffer, a1 = size) -> a0 = length. A line editor: letters appear as you type, Backspace removes
# the last one, Esc clears the line, Up and Down bring back earlier lines, Enter finishes.
readline:
    addi sp, sp, -32
    sw   ra, 28(sp)
    sw   s0, 24(sp)                 # s0 = buffer
    sw   s1, 20(sp)                 # s1 = size - 1 (room for the 0 at the end)
    sw   s2, 16(sp)                 # s2 = length so far
    sw   s3, 12(sp)                 # s3 = which history line Up / Down is showing
    mv   s0, a0
    addi s1, a1, -1
    li   s2, 0
    la   t0, history_count
    lw   s3, 0(t0)
    sb   zero, 0(s0)                # the text always ends with a 0 byte
rl_loop:
    call getkey
    beqz a0, rl_done                # stop requested
    andi t0, a0, 0xff               # ASCII
    srli t1, a0, 8
    andi t1, t1, 0xff               # scancode
    li   t2, 13
    beq  t0, t2, rl_done            # Enter
    li   t2, 10
    beq  t0, t2, rl_done
    li   t2, 8
    beq  t0, t2, rl_backspace
    li   t2, 127
    beq  t0, t2, rl_backspace
    li   t2, 27
    beq  t0, t2, rl_escape
    bnez t0, rl_char
    li   t2, KEY_UP
    beq  t1, t2, rl_up
    li   t2, KEY_DOWN
    beq  t1, t2, rl_down
    j    rl_loop
rl_char:
    li   t2, 32
    blt  t0, t2, rl_loop            # other control keys: ignore
    bge  s2, s1, rl_loop            # line full
    add  t3, s0, s2
    sb   t0, 0(t3)
    addi s2, s2, 1
    add  t3, s0, s2
    sb   zero, 0(t3)
    mv   a0, t0
    call putc                       # echo it
    j    rl_loop
rl_backspace:
    beqz s2, rl_loop
    addi s2, s2, -1
    add  t3, s0, s2
    sb   zero, 0(t3)
    la   a0, erase_char             # back, space, back
    call puts
    j    rl_loop
rl_escape:
    call rl_erase_all
    j    rl_loop
rl_up:
    addi t1, s3, -1                 # one line older...
    bltz t1, rl_loop
    la   t0, history_count
    lw   t0, 0(t0)
    addi t0, t0, -HISTORY_LINES     # ...but only the last 8 lines are kept
    blt  t1, t0, rl_loop
    mv   s3, t1
    j    rl_show_history
rl_down:
    la   t0, history_count
    lw   t0, 0(t0)
    addi t1, s3, 1
    bge  t1, t0, rl_escape_down     # past the newest: an empty line
    mv   s3, t1
    j    rl_show_history
rl_escape_down:
    mv   s3, t0
    call rl_erase_all
    j    rl_loop
rl_show_history:
    call rl_erase_all
    # history line number s3 (0 = oldest kept) is stored at history + (s3 mod 8) * 80
    andi t0, s3, HISTORY_LINES - 1
    li   t1, HISTORY_WIDTH
    mul  t0, t0, t1
    la   t1, history
    add  t1, t1, t0                 # t1 = the remembered line
2:  lbu  t2, 0(t1)
    beqz t2, rl_loop
    bge  s2, s1, rl_loop
    add  t3, s0, s2
    sb   t2, 0(t3)
    addi s2, s2, 1
    add  t3, s0, s2
    sb   zero, 0(t3)
    addi t1, t1, 1
    sw   t1, 8(sp)
    mv   a0, t2
    call putc
    lw   t1, 8(sp)
    j    2b
rl_done:
    add  t3, s0, s2
    sb   zero, 0(t3)
    call newline
    beqz s2, 3f                     # remember non-empty lines
    la   t0, history_count
    lw   t1, 0(t0)
    andi t2, t1, HISTORY_LINES - 1
    li   t3, HISTORY_WIDTH
    mul  t2, t2, t3
    la   t3, history
    add  t3, t3, t2                 # where this line goes
    mv   t4, s0
    li   t5, HISTORY_WIDTH - 1
4:  lbu  t6, 0(t4)
    sb   t6, 0(t3)
    beqz t6, 5f
    addi t4, t4, 1
    addi t3, t3, 1
    addi t5, t5, -1
    bnez t5, 4b
    sb   zero, 0(t3)
5:  addi t1, t1, 1
    sw   t1, 0(t0)
3:  mv   a0, s2
    lw   s3, 12(sp)
    lw   s2, 16(sp)
    lw   s1, 20(sp)
    lw   s0, 24(sp)
    lw   ra, 28(sp)
    addi sp, sp, 32
    ret

# rl_erase_all: remove the whole line from the screen and the buffer (uses readline's s0 and s2)
rl_erase_all:
    addi sp, sp, -16
    sw   ra, 12(sp)
1:  beqz s2, 2f
    addi s2, s2, -1
    la   a0, erase_char
    call puts
    j    1b
2:  sb   zero, 0(s0)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

.data
.align 2
keybuf:          .space KEYBUF_SIZE * 4
keybuf_head:     .word 0
keybuf_tail:     .word 0
break_requested: .word 0
history_count:   .word 0            # how many lines were ever entered (the last 8 are kept)
history:         .space HISTORY_LINES * HISTORY_WIDTH
erase_char:      .asciz "\b \b"
.text
