# csr.s - Zicsr: read / write / set / clear, the immediate forms, counters, and the machine-mode CSRs
.include "_test.s"

.text
_start:
    li   gp, 1
    li   x1, 0x12345678
    csrrw x14, mscratch, x1         # mscratch starts at 0
    bnez x14, fail
    csrr x15, mscratch
    bne  x15, x1, fail
    li   gp, 2
    li   x2, 0xf0000000
    csrrs x14, mscratch, x2         # returns the old value, sets bits
    bne  x14, x1, fail
    csrr x15, mscratch
    li   x7, 0xf2345678
    bne  x15, x7, fail
    li   gp, 3
    csrrc x14, mscratch, x2         # clears bits
    bne  x14, x7, fail
    csrr x15, mscratch
    li   x7, 0x02345678
    bne  x15, x7, fail
    li   gp, 4
    csrrwi x14, mscratch, 5
    bne  x14, x7, fail
    csrr x15, mscratch
    TEST_EQ 5, x15, 5
    li   gp, 6
    csrrsi x14, mscratch, 0x1a
    TEST_EQ 6, x14, 5
    csrr x15, mscratch
    TEST_EQ 7, x15, 31
    csrrci x14, mscratch, 0x0f
    TEST_EQ 8, x14, 31
    csrr x15, mscratch
    TEST_EQ 9, x15, 16
    # csrrs / csrrc with x0 don't write (so they also work on read-only CSRs)
    csrrs x14, mhartid, x0
    TEST_EQ 10, x14, 0
    csrr x14, misa                  # RV32 with I and M
    TEST_EQ 11, x14, 0x40001100
    # mstatus: only MIE, MPIE and MPP exist here, and MPP is always 3 (machine mode)
    li   x1, -1
    csrw mstatus, x1
    csrr x14, mstatus
    TEST_EQ 12, x14, 0x1888
    csrw mstatus, x0
    csrr x14, mstatus
    TEST_EQ 13, x14, 0x1800
    # mepc: the low two bits are always 0 (all instructions are 4 bytes)
    li   x1, 0x80000003
    csrw mepc, x1
    csrr x14, mepc
    TEST_EQ 14, x14, 0x80000000
    la   x1, _start
    csrw mtvec, x1
    csrr x14, mtvec
    li   gp, 15
    bne  x14, x1, fail
    # mie: the software, timer and external interrupt bits
    li   x1, -1
    csrw mie, x1
    csrr x14, mie
    TEST_EQ 16, x14, 0x888
    csrw mie, x0
    # minstret counts retired instructions
    csrr x1, minstret
    nop
    nop
    csrr x2, minstret
    sub  x14, x2, x1
    TEST_EQ 17, x14, 3
    rdinstret x1
    nop
    rdinstret x2
    sub  x14, x2, x1
    TEST_EQ 18, x14, 2
    # mcycle goes up too
    csrr x1, mcycle
    nop
    csrr x2, mcycle
    sub  x14, x2, x1
    li   gp, 19
    blez x14, fail
    # time (mtime) goes up
    rdtime x1
    nop
    nop
    rdtime x2
    li   gp, 20
    bleu x2, x1, fail
    # writing minstret
    li   x1, 1000
    csrw minstret, x1
    csrr x14, minstret
    TEST_EQ 21, x14, 1000           # the instruction that writes minstret isn't counted on top of the new value
    TEST_DONE
