# kernel.s - LabOS, a tiny operating system for Emulator Lab's RISC-V computer.
#
# The boot ROM copies LabOS to the start of RAM (0x80000000) and jumps here. LabOS then:
#   1. shows a banner and tests the memory
#   2. sets up traps: the timer interrupt (a clock that ticks 100 times a second), the keyboard interrupt, and
#      system calls (ecall) that programs use to ask LabOS for help
#   3. checks the disk (and formats it the first time)
#   4. starts the shell, which reads commands and runs them
#
# How the code is organised (each file is included below):
#   console.s   printing text and numbers
#   keyboard.s  the keyboard buffer and the line editor (with history)
#   trap.s      the trap handler: interrupts, system calls, crashes; the timer
#   disk.s      the disk driver and the little file system
#   gfx.s       drawing on the 320 x 200 screen
#   lib.s       small helpers: comparing and copying text, reading numbers, dates
#   shell.s     the command line and its commands
#
# Calling convention (the standard RISC-V one): arguments in a0-a7, the answer in a0, a function may change
# t0-t6 and a0-a7, but must put back s0-s11, sp and ra as it found them.

.include "memmap.s"

.text
_start:
    li   sp, KERNEL_STACK_TOP       # our own stack
    la   gp, kernel_globals         # gp points at LabOS's variables (not used much, but a good habit)

    call console_init
    call show_banner
    call memory_test
    call trap_init                  # from here on the timer ticks and keys arrive by interrupt
    call fs_init
    call random_init

    la   a0, msg_ready
    call puts
    j    shell                      # the shell never returns

# ---------------------------------------------------------------------------------------------------------------
# show_banner: clear the screen and draw the LabOS logo
show_banner:
    addi sp, sp, -16
    sw   ra, 12(sp)
    call clear_screen
    li   a0, 11                     # light cyan
    li   a1, 0
    call set_color
    la   a0, banner
    call puts
    li   a0, 14                     # yellow
    li   a1, 0
    call set_color
    la   a0, banner_line
    call puts
    call default_color
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# ---------------------------------------------------------------------------------------------------------------
# memory_test: write a pattern to every word of free RAM, then read it all back. Prints a # per megabyte.
# The pattern is the address mixed with 0xA5A5A5A5, so each word gets a different value and a broken address
# line would show up.
memory_test:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    sw   s2, 0(sp)
    la   a0, msg_memtest
    call puts
    li   s0, MEMTEST_START
    li   s1, MEMTEST_END
    li   s2, 0xA5A5A5A5
1:  # write one megabyte, four words at a time
    li   t0, 0x100000
    add  t1, s0, t0                 # t1 = end of this megabyte
    mv   t2, s0
2:  xor  t3, t2, s2
    sw   t3, 0(t2)
    addi t4, t2, 4
    xor  t4, t4, s2
    sw   t4, 4(t2)
    addi t5, t2, 8
    xor  t5, t5, s2
    sw   t5, 8(t2)
    addi t6, t2, 12
    xor  t6, t6, s2
    sw   t6, 12(t2)
    addi t2, t2, 16
    bltu t2, t1, 2b
    # read it back
    mv   t2, s0
3:  lw   t3, 0(t2)
    xor  t3, t3, s2
    bne  t3, t2, memory_bad
    lw   t4, 4(t2)
    xor  t4, t4, s2
    addi t5, t2, 4
    bne  t4, t5, memory_bad
    lw   t4, 8(t2)
    xor  t4, t4, s2
    addi t5, t2, 8
    bne  t4, t5, memory_bad
    lw   t4, 12(t2)
    xor  t4, t4, s2
    addi t5, t2, 12
    bne  t4, t5, memory_bad
    addi t2, t2, 16
    bltu t2, t1, 3b
    mv   s0, t1                     # next megabyte (before calling putc, which may change t registers)
    li   a0, '#'
    call putc
    bltu s0, s1, 1b
    # all good
    la   a0, msg_memok
    call puts
    li   t0, 1
    la   t1, memory_ok
    sw   t0, 0(t1)
    j    4f
memory_bad:
    mv   s0, t2
    la   a0, msg_membad
    call puts
    mv   a0, s0
    call put_hex
    call newline
4:  lw   s2, 0(sp)
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

.include "console.s"
.include "keyboard.s"
.include "trap.s"
.include "disk.s"
.include "gfx.s"
.include "lib.s"
.include "shell.s"

# ---------------------------------------------------------------------------------------------------------------
.data
.align 2
kernel_globals:
memory_ok:  .word 0

banner:
    .ascii " _                _      ___  ____  \n"
    .ascii "| |       __ _   | |__  / _ \\/ ___| \n"
    .ascii "| |      / _` |  | '_ \\| | | \\___ \\ \n"
    .ascii "| |___  | (_| |  | |_) | |_| |___) |\n"
    .asciz "|_____|  \\__,_|  |_.__/ \\___/|____/ \n"
banner_line:
    .asciz "\nLabOS 1.0 - a tiny operating system for a RISC-V computer, made in Emulator Lab\n\n"
msg_memtest: .asciz "Testing memory  "
msg_memok:   .asciz " OK: 16 MB of RAM\n"
msg_membad:  .asciz "\nMemory error at "
msg_ready:   .asciz "Type help to see the commands.\n\n"

.align 4
kernel_end:                         # everything LabOS uses (code and data) ends here
