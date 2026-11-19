# trap.s - traps: interrupts, system calls and crashes.
#
# A "trap" is the processor stopping what it was doing and jumping to the trap handler (the address in the CSR
# mtvec). It happens for:
#   - interrupts: something outside needs attention (the timer, a key press)
#   - ecall: a program asks LabOS for help (a "system call"); a7 says which service, a0-a4 are the details
#   - exceptions: the program did something impossible (an unknown instruction, a bad memory address...)
# The processor saves where it was in mepc and why in mcause (the top bit set means an interrupt), then
# trap_entry saves every register, works out what to do, puts the registers back and returns with mret.

.equ TICK_US,      10000            # the timer ticks every 10 000 microseconds: 100 times a second
.equ FRAME_SIZE,   128              # 32 registers x 4 bytes

# where each register is kept in the trap frame (register number x 4)
.equ F_RA, 4
.equ F_SP, 8
.equ F_A0, 40
.equ F_A1, 44
.equ F_A2, 48
.equ F_A3, 52
.equ F_A4, 56
.equ F_A7, 68

.text
# trap_init: point mtvec at our handler, start the timer, and switch on the timer and keyboard interrupts
trap_init:
    addi sp, sp, -16
    sw   ra, 12(sp)
    la   t0, trap_entry
    csrw mtvec, t0                  # traps go to trap_entry (mode 0: one handler for everything)
    li   t0, TRAP_STACK_TOP
    csrw mscratch, t0               # trap_entry swaps sp with mscratch: this is the handler's stack
    # first alarm one tick from now
    li   t0, CLINT_MTIME
    lw   t1, 0(t0)                  # now: low word
    lw   t2, 4(t0)                  #      high word
    li   t0, TICK_US
    add  t4, t1, t0                 # alarm = now + one tick (64-bit: a carry goes into the high word)
    sltu t0, t4, t1
    add  t5, t2, t0
    call set_alarm
    li   t0, KBD_BASE
    li   t1, 1
    sw   t1, KBD_CTRL(t0)           # the keyboard interrupts when a key arrives
    li   t0, (1 << 7) | (1 << 11)   # mie: MTIE (timer) and MEIE (external: keyboard)
    csrs mie, t0
    csrsi mstatus, 8                # mstatus.MIE: interrupts on
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# set_alarm: mtimecmp = t5:t4 (high:low). The high word is first set to all ones, so the alarm can't go off
# half-way through changing it.
set_alarm:
    li   t0, CLINT_MTIMECMP
    li   t1, -1
    sw   t1, 4(t0)
    sw   t4, 0(t0)
    sw   t5, 4(t0)
    ret

# timer_poll: if the alarm time has come, count a tick and set the next alarm. Called by the timer interrupt and
# by every waiting loop (inside a system call interrupts stay off, so they call it themselves).
# mtime and mtimecmp are 64-bit numbers, but our registers are 32-bit, so each is handled as two halves.
timer_poll:
1:  li   t0, CLINT_MTIME
    lw   t2, 4(t0)                  # now: high word
    lw   t1, 0(t0)                  #      low word
    lw   t3, 4(t0)
    bne  t2, t3, 1b                 # the high word changed while we read: read again
    li   t0, CLINT_MTIMECMP
    lw   t4, 0(t0)                  # alarm: low word
    lw   t5, 4(t0)                  #        high word
    bltu t2, t5, 9f                 # now < alarm: nothing to do
    bne  t2, t5, 2f
    bltu t1, t4, 9f
2:  la   t0, ticks
    lw   t3, 0(t0)
    li   t6, 1000                   # catch up at most 1000 ticks (10 seconds) if time jumped ahead
3:  addi t3, t3, 1                  # count a tick
    li   t0, TICK_US
    add  t0, t4, t0                 # alarm += one tick...
    sltu t4, t0, t4                 # ...with the carry...
    add  t5, t5, t4                 # ...into the high word
    mv   t4, t0
    addi t6, t6, -1
    beqz t6, 5f
    bltu t5, t2, 3b                 # is the new alarm still in the past? Then another tick.
    bne  t5, t2, 4f
    bgeu t1, t4, 3b
    j    4f
5:  li   t0, TICK_US                # too far behind: the next alarm is one tick from now
    add  t4, t1, t0
    sltu t0, t4, t1
    add  t5, t2, t0
4:  la   t0, ticks
    sw   t3, 0(t0)
    j    set_alarm                  # (set_alarm returns straight to our caller)
9:  ret

# ---------------------------------------------------------------------------------------------------------------
# trap_entry: every trap starts here. The registers belong to whatever was interrupted, so save them all first.
.align 2
trap_entry:
    csrrw sp, mscratch, sp          # swap: sp = the trap stack, mscratch = the interrupted program's sp
    addi sp, sp, -FRAME_SIZE
    sw   x1, 4(sp)
    sw   x3, 12(sp)
    sw   x4, 16(sp)
    sw   x5, 20(sp)
    sw   x6, 24(sp)
    sw   x7, 28(sp)
    sw   x8, 32(sp)
    sw   x9, 36(sp)
    sw   x10, 40(sp)
    sw   x11, 44(sp)
    sw   x12, 48(sp)
    sw   x13, 52(sp)
    sw   x14, 56(sp)
    sw   x15, 60(sp)
    sw   x16, 64(sp)
    sw   x17, 68(sp)
    sw   x18, 72(sp)
    sw   x19, 76(sp)
    sw   x20, 80(sp)
    sw   x21, 84(sp)
    sw   x22, 88(sp)
    sw   x23, 92(sp)
    sw   x24, 96(sp)
    sw   x25, 100(sp)
    sw   x26, 104(sp)
    sw   x27, 108(sp)
    sw   x28, 112(sp)
    sw   x29, 116(sp)
    sw   x30, 120(sp)
    sw   x31, 124(sp)
    csrr t0, mscratch
    sw   t0, 8(sp)                  # the interrupted sp
    mv   a0, sp                     # a0 = the frame: the handler reads and changes the saved registers here
    call trap_handler
    addi t0, sp, FRAME_SIZE
    csrw mscratch, t0               # ready for the next trap
    lw   x1, 4(sp)
    lw   x3, 12(sp)
    lw   x4, 16(sp)
    lw   x5, 20(sp)
    lw   x6, 24(sp)
    lw   x7, 28(sp)
    lw   x8, 32(sp)
    lw   x9, 36(sp)
    lw   x10, 40(sp)
    lw   x11, 44(sp)
    lw   x12, 48(sp)
    lw   x13, 52(sp)
    lw   x14, 56(sp)
    lw   x15, 60(sp)
    lw   x16, 64(sp)
    lw   x17, 68(sp)
    lw   x18, 72(sp)
    lw   x19, 76(sp)
    lw   x20, 80(sp)
    lw   x21, 84(sp)
    lw   x22, 88(sp)
    lw   x23, 92(sp)
    lw   x24, 96(sp)
    lw   x25, 100(sp)
    lw   x26, 104(sp)
    lw   x27, 108(sp)
    lw   x28, 112(sp)
    lw   x29, 116(sp)
    lw   x30, 120(sp)
    lw   x31, 124(sp)
    lw   sp, 8(sp)                  # last: back to the interrupted sp
    mret                            # continue at mepc

# trap_handler(a0 = frame): decide what kind of trap this is
trap_handler:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    mv   s0, a0
    csrr t0, mcause
    bltz t0, th_interrupt           # top bit set: an interrupt
    li   t1, 11
    beq  t0, t1, th_syscall         # 11: ecall from machine mode
    j    th_exception
th_interrupt:
    slli t0, t0, 1                  # drop the top bit
    srli t0, t0, 1
    li   t1, 7
    bne  t0, t1, 1f
    call timer_poll                 # 7: timer
    j    th_done
1:  li   t1, 11
    bne  t0, t1, th_done
    call kbd_poll                   # 11: external (the keyboard)
    j    th_done

th_syscall:
    csrr t0, mepc                   # return to the instruction after ecall, not to ecall itself
    addi t0, t0, 4
    csrw mepc, t0
    lw   t0, F_A7(s0)               # which service
    li   t1, SYSCALL_COUNT
    bgeu t0, t1, th_bad_syscall
    la   t1, syscall_table
    slli t0, t0, 2
    add  t1, t1, t0
    lw   t1, 0(t1)                  # the function for this service
    lw   a0, F_A0(s0)               # its arguments, from the caller's saved registers
    lw   a1, F_A1(s0)
    lw   a2, F_A2(s0)
    lw   a3, F_A3(s0)
    lw   a4, F_A4(s0)
    jalr t1
    sw   a0, F_A0(s0)               # the answer goes back in the caller's a0
    j    th_done
th_bad_syscall:
    li   t0, -1
    sw   t0, F_A0(s0)
    j    th_done

th_exception:
    la   t0, program_running
    lw   t0, 0(t0)
    beqz t0, kernel_panic           # LabOS itself crashed
    # a program crashed: say why, then go back to the shell
    li   a0, 12                     # light red
    li   a1, 0
    call set_color
    la   a0, msg_crash
    call puts
    csrr a0, mcause
    call put_cause
    la   a0, msg_at
    call puts
    csrr a0, mepc
    call put_hex
    csrr t0, mcause
    li   t1, 2
    bne  t0, t1, 1f
    la   a0, msg_inst               # illegal instruction: mtval holds the instruction
    call puts
    csrr a0, mtval
    call put_hex
1:  li   t1, 4                      # misaligned / access fault: mtval holds the address
    blt  t0, t1, 2f
    li   t1, 7
    bgt  t0, t1, 2f
    la   a0, msg_addr
    call puts
    csrr a0, mtval
    call put_hex
2:  call newline
    call default_color
    li   a0, -1
    j    th_end_program

th_done:
    # Ctrl+C pressed while a program runs: stop it on the way out of the trap
    la   t0, break_requested
    lw   t1, 0(t0)
    beqz t1, 1f
    sw   zero, 0(t0)
    la   t0, program_running
    lw   t0, 0(t0)
    beqz t0, 1f
    la   a0, msg_break
    call puts
    li   a0, -2
    j    th_end_program
1:  lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# th_end_program(a0 = exit code): instead of returning into the program, mret goes to program_return in the
# shell, with the exit code in a0. mstatus.MPIE = 1 so interrupts are on again there.
th_end_program:
    sw   a0, F_A0(s0)
    la   t0, program_return
    csrw mepc, t0
    li   t0, 0x80
    csrs mstatus, t0
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# kernel_panic: LabOS itself hit an exception. Nothing sensible can continue: show the details and stop.
kernel_panic:
    li   a0, 15
    li   a1, 4                      # white on red
    call set_color
    la   a0, msg_panic
    call puts
    csrr a0, mcause
    call put_cause
    la   a0, msg_at
    call puts
    csrr a0, mepc
    call put_hex
    la   a0, msg_tval
    call puts
    csrr a0, mtval
    call put_hex
    call newline
    la   a0, msg_halted
    call puts
1:  wfi
    j    1b

# put_cause(a0 = mcause): the exception's name
put_cause:
    li   t0, 8
    bltu a0, t0, 1f
    li   a0, 8                      # anything else: "unknown"
1:  slli a0, a0, 2
    la   t0, cause_names
    add  t0, t0, a0
    lw   a0, 0(t0)
    j    puts

# ---------------------------------------------------------------------------------------------------------------
# System calls. A program puts the service number in a7 and the details in a0-a4, then runs ecall.
# (Linux on RISC-V works the same way, with its own numbers: 93 is exit, 64 is write.)
#   1 exit(code)               2 putchar(c)               3 print(text)              4 print_num(n)
#   5 getkey() -> key (waits)  6 pollkey() -> key or 0    7 readline(buf, size) -> length
#   8 sleep(ms)                9 time_ms() -> ms since start                         10 random() -> number
#  11 screen_mode(m)          12 clear_gfx(colour)       13 pixel(x, y, colour)
#  14 rect(x, y, w, h, colour)  15 set_palette(index, 0xRRGGBB)  16 print_hex(n)
#  17 set_color(text, background)  18 clear_text()       19 get_pixel(x, y) -> colour
.equ SYSCALL_COUNT, 20

sys_exit:
    j    th_end_program_from_syscall
sys_print_num:
    j    put_dec
sys_time_ms:
    la   t0, ticks
    lw   a0, 0(t0)
    li   t0, 10
    mul  a0, a0, t0
    ret
sys_bad:
    li   a0, -1
    ret

# exit from inside the dispatcher: unwind to trap_handler's frame (its s0 and stack are still there)
th_end_program_from_syscall:
    j    th_end_program

# sleep(a0 = milliseconds): rest with wfi until enough timer ticks have passed
sleep_ms:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    addi a0, a0, 9
    li   t0, 10
    divu a0, a0, t0                 # ticks to wait, rounded up
    la   t0, ticks
    lw   t1, 0(t0)
    add  s0, t1, a0                 # the tick to wake up at
1:  la   t0, ticks
    lw   t1, 0(t0)
    sub  t1, t1, s0
    bgez t1, 2f
    la   t0, break_requested
    lw   t0, 0(t0)
    bnez t0, 2f
    csrrci t2, mstatus, 8           # interrupts off (remember if they were on)...
    sw   t2, 4(sp)
    wfi                             # ...rest until the timer or a key...
    call timer_poll
    call kbd_poll
    lw   t2, 4(sp)
    andi t2, t2, 8
    csrs mstatus, t2                # ...and interrupts back on if they were
    j    1b
2:  lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# random() -> a0: a "xorshift" random number generator: shifting and mixing the bits of the last number
random_next:
    la   t0, random_state
    lw   a0, 0(t0)
    slli t1, a0, 13
    xor  a0, a0, t1
    srli t1, a0, 17
    xor  a0, a0, t1
    slli t1, a0, 5
    xor  a0, a0, t1
    sw   a0, 0(t0)
    ret

# random_init: start the generator from the clock (it must never be 0)
random_init:
    li   t0, RTC_BASE
    lw   t1, 0(t0)
    rdcycle t2
    xor  t1, t1, t2
    bnez t1, 1f
    li   t1, 0x12345
1:  la   t0, random_state
    sw   t1, 0(t0)
    ret

sys_clear_text:
    j    clear_screen
sys_set_color:
    j    set_color

.data
.align 2
syscall_table:
    .word sys_bad, sys_exit, putc, puts, sys_print_num, getkey, pollkey, readline
    .word sleep_ms, sys_time_ms, random_next, gfx_mode, gfx_clear, gfx_pixel, gfx_rect, gfx_palette
    .word put_hex, sys_set_color, sys_clear_text, gfx_get_pixel

ticks:            .word 0           # timer ticks since LabOS started (100 a second)
random_state:     .word 1
program_running:  .word 0

cause_names:
    .word c0, c1, c2, c3, c4, c5, c6, c7, c8
c0: .asciz "Jump to a misaligned address"
c1: .asciz "No memory at the instruction address"
c2: .asciz "Illegal instruction"
c3: .asciz "Breakpoint (ebreak)"
c4: .asciz "Misaligned memory read"
c5: .asciz "Memory read from an address with nothing there"
c6: .asciz "Misaligned memory write"
c7: .asciz "Memory write to an address that can't be written"
c8: .asciz "Unknown problem"

msg_crash:  .asciz "\nThe program crashed: "
msg_at:     .asciz " at "
msg_inst:   .asciz ", instruction "
msg_addr:   .asciz ", address "
msg_tval:   .asciz ", mtval "
msg_break:  .asciz "^C\n"
msg_panic:  .asciz "\n*** LabOS crashed: "
msg_halted: .asciz "\nThe computer has stopped. Use Reset to start again.\n"
.text
