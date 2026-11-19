# timer.s - interrupts: the timer (CLINT mtime / mtimecmp), wfi, masking with mstatus.MIE, the software
# interrupt (msip), and vectored mode.
.include "_test.s"

.equ MTIME,    0x0200BFF8
.equ MTIMECMP, 0x02004000
.equ MSIP,     0x02000000

# ALARM_IN: mtimecmp = mtime + n
.macro ALARM_IN n
    li   t0, MTIME
    lw   t1, 0(t0)
    lw   t2, 4(t0)
    addi t1, t1, \n
    li   t0, MTIMECMP
    li   t3, -1
    sw   t3, 4(t0)
    sw   t1, 0(t0)
    sw   t2, 4(t0)
.endm

.text
_start:
    la   t0, handler
    csrw mtvec, t0
    # 1. a timer interrupt arrives while the program loops
    ALARM_IN 200
    li   t0, 0x80
    csrs mie, t0                    # MTIE
    csrsi mstatus, 8                # MIE
    li   gp, 1
loop1:
    la   t2, fired
    lw   t3, 0(t2)
    beqz t3, loop1
loop1_end:
    li   gp, 2
    la   t2, got
    lw   t3, 0(t2)
    li   t4, 0x80000007             # interrupt bit + 7 (machine timer)
    bne  t3, t4, fail
    li   gp, 3
    lw   t3, 4(t2)                  # mepc: somewhere in the loop (the instruction that didn't run yet)
    la   t4, loop1
    bltu t3, t4, fail
    la   t4, loop1_end
    bgtu t3, t4, fail

    # 2. wfi sleeps until the interrupt; mepc is the instruction after wfi
    la   t2, fired
    sw   zero, 0(t2)
    ALARM_IN 500
t_wfi:
    wfi
t_after:
    li   gp, 4
    la   t2, fired
    lw   t3, 0(t2)
    beqz t3, fail
    li   gp, 5
    la   t2, got
    lw   t3, 4(t2)
    la   t4, t_after
    bne  t3, t4, fail

    # 3. with mstatus.MIE off, the interrupt waits (mip shows it), wfi still wakes up
    csrci mstatus, 8
    la   t2, fired
    sw   zero, 0(t2)
    ALARM_IN 100
    wfi
    li   gp, 6
    la   t2, fired
    lw   t3, 0(t2)
    bnez t3, fail                   # not taken
    csrr t3, mip
    andi t3, t3, 0x80
    li   gp, 7
    beqz t3, fail                   # but pending
    csrsi mstatus, 8                # interrupts on: taken right away
    nop
    li   gp, 8
    lw   t3, 0(t2)
    beqz t3, fail

    # 4. the software interrupt: writing 1 to msip
    la   t2, fired
    sw   zero, 0(t2)
    li   t0, 0x08
    csrs mie, t0                    # MSIE
    li   t0, MSIP
    li   t1, 1
    sw   t1, 0(t0)
    nop
    li   gp, 9
    lw   t3, 0(t2)
    beqz t3, fail
    la   t2, got
    lw   t3, 0(t2)
    li   t4, 0x80000003
    li   gp, 10
    bne  t3, t4, fail

    # 5. vectored mode: interrupt number 7 goes to base + 4 * 7
    la   t0, vectors
    ori  t0, t0, 1
    csrw mtvec, t0
    la   t2, fired
    sw   zero, 0(t2)
    ALARM_IN 100
    wfi
    li   gp, 11
    la   t2, fired
    lw   t3, 0(t2)
    li   t4, 2                      # the vectored handler writes 2
    bne  t3, t4, fail
    TEST_DONE

.align 2
handler:
    la   t5, got
    csrr t6, mcause
    sw   t6, 0(t5)
    csrr t6, mepc
    sw   t6, 4(t5)
    li   t6, 1
    la   t5, fired
    sw   t6, 0(t5)
    # quiet the interrupt that came: the timer (alarm far away) or msip
    li   t5, MTIMECMP
    li   t6, -1
    sw   t6, 4(t5)
    sw   t6, 0(t5)
    li   t5, MSIP
    sw   zero, 0(t5)
    mret

.align 6
vectors:
    j    fail                       # 0: exceptions (none expected here)
    j    fail
    j    fail
    j    fail                       # 3: software
    j    fail
    j    fail
    j    fail
    j    vec_timer                  # 7: timer
    j    fail
    j    fail
    j    fail
    j    fail                       # 11: external

vec_timer:
    li   t6, 2
    la   t5, fired
    sw   t6, 0(t5)
    li   t5, MTIMECMP
    li   t6, -1
    sw   t6, 4(t5)
    sw   t6, 0(t5)
    mret

.data
.align 2
got:   .word 0, 0
fired: .word 0
