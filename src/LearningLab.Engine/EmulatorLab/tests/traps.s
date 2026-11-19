# traps.s - exceptions: ecall, ebreak, illegal instructions, misaligned and faulting loads / stores / jumps,
# unknown and read-only CSRs, and what mret and trap entry do to mstatus.
# The handler writes down mcause, mepc, mtval and mstatus, then skips the instruction that trapped.
.include "_test.s"

# CHECK_TRAP: the last trap had this cause, at this instruction, with this mtval
.macro CHECK_TRAP num, cause, at, tval
    li   gp, \num
    la   t2, got
    lw   t3, 0(t2)
    li   t4, \cause
    bne  t3, t4, fail
    lw   t3, 4(t2)
    la   t4, \at
    bne  t3, t4, fail
    lw   t3, 8(t2)
    li   t4, \tval
    bne  t3, t4, fail
    li   t4, -1
    sw   t4, 0(t2)                  # forget it, ready for the next check
.endm

# NO_TRAP: nothing trapped since the last check
.macro NO_TRAP num
    li   gp, \num
    la   t2, got
    lw   t3, 0(t2)
    li   t4, -1
    bne  t3, t4, fail
.endm

.text
_start:
    la   t0, handler
    csrw mtvec, t0
    la   t2, got
    li   t4, -1
    sw   t4, 0(t2)

t_ecall:
    ecall
    CHECK_TRAP 1, 11, t_ecall, 0
t_ebreak:
    ebreak
    CHECK_TRAP 2, 3, t_ebreak, t_ebreak         # mtval = the address of the ebreak
t_zero:
    .word 0x00000000                            # all zeros is not an instruction
    CHECK_TRAP 3, 2, t_zero, 0
t_ones:
    .word 0xffffffff
    CHECK_TRAP 4, 2, t_ones, 0xffffffff         # mtval = the instruction's bits
t_slli:
    .word 0x02009093                            # slli with shift amount 32: not allowed on RV32
    CHECK_TRAP 5, 2, t_slli, 0x02009093

    # misaligned memory accesses trap (cause 4 load, 6 store) with mtval = the address
    la   x5, buf
t_lw:
    lw   x6, 1(x5)
    CHECK_TRAP 6, 4, t_lw, buf + 1
t_lh:
    lh   x6, 3(x5)
    CHECK_TRAP 7, 4, t_lh, buf + 3
    lh   x6, 2(x5)                              # 2-byte aligned: fine
    lbu  x6, 3(x5)
    NO_TRAP 8
t_sw:
    sw   x6, 2(x5)
    CHECK_TRAP 9, 6, t_sw, buf + 2
t_sh:
    sh   x6, 1(x5)
    CHECK_TRAP 10, 6, t_sh, buf + 1

    # access faults: nothing at 0x40000000 (cause 5), the boot ROM can't be written (cause 7)
    li   x5, 0x40000000
t_lf:
    lw   x6, 0(x5)
    CHECK_TRAP 11, 5, t_lf, 0x40000000
    li   x5, 0x00001000
t_sf:
    sw   x6, 0(x5)
    CHECK_TRAP 12, 7, t_sf, 0x00001000

    # jumping to an address that isn't a multiple of 4 traps on the jump itself (cause 0), mtval = the target
    la   x5, t_target
t_jalr:
    jalr x8, 2(x5)
    CHECK_TRAP 13, 0, t_jalr, t_target + 2
    li   x8, 77
t_jal:
    jal  x8, . + 6
    CHECK_TRAP 14, 0, t_jal, t_jal + 6
    TEST_EQ 15, x8, 77                          # rd is not written when the jump traps
t_beq:
    beq  x0, x0, . + 6
    CHECK_TRAP 16, 0, t_beq, t_beq + 6
    bne  x0, x0, . + 6                          # not taken: no trap
    NO_TRAP 17

    # CSRs: an unknown one, and writing a read-only one, are illegal instructions
t_csr:
    csrr x6, 0x7c0
    la   t0, t_csr
    lw   t1, 0(t0)
    la   t2, got
    lw   t3, 8(t2)
    li   gp, 18
    bne  t1, t3, fail
    CHECK_TRAP 19, 2, t_csr, 0x7c002373
t_ro:
    csrw mhartid, x0
    CHECK_TRAP 20, 2, t_ro, 0xf1401073
    csrr x6, mhartid                            # reading it is fine
    NO_TRAP 21

    # trap entry: MPIE = old MIE, MIE = 0, MPP = 3
    csrsi mstatus, 8                            # MIE on (mie is 0, so no interrupt can come)
t_mstat:
    ecall
    li   gp, 22
    la   t2, got
    lw   t3, 12(t2)                             # mstatus inside the handler
    li   t4, 0x1880
    bne  t3, t4, fail
    csrr t3, mstatus                            # after mret: MIE = 1 again, MPIE = 1
    li   t4, 0x1888
    bne  t3, t4, fail
    csrci mstatus, 8
    # mret jumps to mepc and sets MIE = MPIE
    li   t0, 0x80
    csrs mstatus, t0                            # MPIE = 1
    la   t0, t_after_mret
    csrw mepc, t0
    mret
    j    fail
t_after_mret:
    csrr t3, mstatus
    li   gp, 23
    andi t3, t3, 8
    beqz t3, fail
    csrci mstatus, 8
    # vectored mode: exceptions still go to the base address
    la   t0, handler
    ori  t0, t0, 1
    csrw mtvec, t0
    la   t2, got
    li   t4, -1
    sw   t4, 0(t2)
t_vec:
    ecall
    CHECK_TRAP 24, 11, t_vec, 0
    TEST_DONE

t_target:
    j    fail
    j    fail

.align 2
handler:
    la   t5, got
    csrr t6, mcause
    sw   t6, 0(t5)
    csrr t6, mepc
    sw   t6, 4(t5)
    csrr t6, mtval
    sw   t6, 8(t5)
    csrr t6, mstatus
    sw   t6, 12(t5)
    csrr t6, mepc
    addi t6, t6, 4                              # carry on after the instruction that trapped
    csrw mepc, t6
    mret

.data
.align 2
got: .word -1, 0, 0, 0
buf: .word 0x11223344, 0x55667788
