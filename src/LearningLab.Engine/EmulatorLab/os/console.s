# console.s - printing text. Everything goes out through the serial port (UART); the window shows it as the
# text console. Colours and clearing the screen use "ANSI escape codes": the byte 27 (ESC) followed by [ and a
# short command, the same codes terminals have understood since the 1970s.

.text
# console_init: nothing to set up on our serial port except the colours
console_init:
    addi sp, sp, -16
    sw   ra, 12(sp)
    li   t0, 7                      # light grey text on black
    la   t1, text_fg
    sw   t0, 0(t1)
    la   t1, text_bg
    sw   zero, 0(t1)
    call default_color
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# putc(a0 = character): send one byte
putc:
    li   t0, UART_BASE
1:  lbu  t1, UART_LSR(t0)           # bit 5 of the line status register: ready for the next byte
    andi t1, t1, 0x20
    beqz t1, 1b
    sb   a0, UART_THR(t0)
    ret

# puts(a0 = address of text ending in a 0 byte)
puts:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    mv   s0, a0
1:  lbu  a0, 0(s0)
    beqz a0, 2f
    call putc
    addi s0, s0, 1
    j    1b
2:  lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

newline:
    li   a0, '\n'
    j    putc                       # a "tail call": putc returns straight to our caller

# put_dec(a0 = number): print a signed number in decimal
put_dec:
    bgez a0, put_udec
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   a0, 8(sp)
    li   a0, '-'
    call putc
    lw   a0, 8(sp)
    neg  a0, a0
    lw   ra, 12(sp)
    addi sp, sp, 16
    # fall through to put_udec

# put_udec(a0 = number): print an unsigned number in decimal. The digits come out backwards (ones first) when
# we divide by 10, so we collect them on the stack and print them in reverse.
put_udec:
    addi sp, sp, -32
    sw   ra, 28(sp)
    sw   s0, 24(sp)
    addi s0, sp, 0                  # s0 = where the next digit goes (up to 10 digits: sp+0 .. sp+9)
    li   t1, 10
1:  remu t2, a0, t1                 # the last digit
    divu a0, a0, t1                 # the rest
    addi t2, t2, '0'
    sb   t2, 0(s0)
    addi s0, s0, 1
    bnez a0, 1b
2:  addi s0, s0, -1                 # print them from the last one stored
    lbu  a0, 0(s0)
    call putc
    bne  s0, sp, 2b
    lw   s0, 24(sp)
    lw   ra, 28(sp)
    addi sp, sp, 32
    ret

# put_dec2(a0 = 0-99): two digits, with a leading zero (for clocks)
put_dec2:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   a0, 8(sp)
    li   t0, 10
    divu a0, a0, t0
    addi a0, a0, '0'
    call putc
    lw   a0, 8(sp)
    li   t0, 10
    remu a0, a0, t0
    addi a0, a0, '0'
    call putc
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# put_hex(a0 = number): print 0x and 8 hex digits
put_hex:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    mv   s0, a0
    li   a0, '0'
    call putc
    li   a0, 'x'
    call putc
    li   s1, 28                     # shift: start with the top 4 bits
1:  srl  t0, s0, s1
    andi a0, t0, 15
    call hex_digit
    call putc
    addi s1, s1, -4
    bgez s1, 1b
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# put_hex2(a0 = byte): two hex digits
put_hex2:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   a0, 8(sp)
    srli a0, a0, 4
    andi a0, a0, 15
    call hex_digit
    call putc
    lw   a0, 8(sp)
    andi a0, a0, 15
    call hex_digit
    call putc
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# hex_digit(a0 = 0-15) -> a0 = '0'-'9' or 'a'-'f'
hex_digit:
    li   t0, 10
    blt  a0, t0, 1f
    addi a0, a0, 'a' - 10
    ret
1:  addi a0, a0, '0'
    ret

# put_spaces(a0 = how many)
put_spaces:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    mv   s0, a0
1:  blez s0, 2f
    li   a0, ' '
    call putc
    addi s0, s0, -1
    j    1b
2:  lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# clear_screen: ESC [ 2 J (clear) and ESC [ H (cursor to the top left)
clear_screen:
    la   a0, esc_clear
    j    puts

# set_color(a0 = text colour 0-15, a1 = background 0-15): PC colour numbers, sent as ANSI codes
# (ANSI numbers its colours in a different order, so a small table translates them).
set_color:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    andi s0, a0, 15
    andi s1, a1, 15
    li   a0, 27
    call putc
    li   a0, '['
    call putc
    # text: 30-37, or 90-97 for the bright half
    andi t0, s0, 7
    la   t1, pc_to_ansi
    add  t1, t1, t0
    lbu  t2, 0(t1)
    li   a0, 30
    andi t3, s0, 8
    beqz t3, 1f
    li   a0, 90
1:  add  a0, a0, t2
    call put_udec
    li   a0, ';'
    call putc
    # background: 40-47, or 100-107
    andi t0, s1, 7
    la   t1, pc_to_ansi
    add  t1, t1, t0
    lbu  t2, 0(t1)
    li   a0, 40
    andi t3, s1, 8
    beqz t3, 2f
    li   a0, 100
2:  add  a0, a0, t2
    call put_udec
    li   a0, 'm'
    call putc
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# default_color: back to the colours chosen with the `color` command
default_color:
    la   t0, text_fg
    lw   a0, 0(t0)
    la   t0, text_bg
    lw   a1, 0(t0)
    j    set_color

.data
.align 2
text_fg:    .word 7
text_bg:    .word 0
esc_clear:  .asciz "\e[2J\e[H"
pc_to_ansi: .byte 0, 4, 2, 6, 1, 5, 3, 7     # PC colour 0-7 -> ANSI colour 0-7
.text
