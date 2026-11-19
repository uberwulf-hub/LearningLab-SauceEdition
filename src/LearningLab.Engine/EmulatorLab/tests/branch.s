# branch.s - branches (signed and unsigned), jal / jalr return addresses, jalr clearing bit 0
.include "_test.s"

.text
_start:
    li   gp, 1
    li   x1, 1
    li   x2, 1
    bne  x1, x2, fail
    beq  x1, x2, 1f
    j    fail
1:  li   gp, 2
    li   x1, -1
    li   x2, 1
    blt  x1, x2, 2f             # -1 < 1 (signed): taken
    j    fail
2:  li   gp, 3
    bltu x1, x2, fail           # 0xffffffff < 1 (unsigned): not taken
    li   gp, 4
    bge  x2, x1, 3f             # 1 >= -1
    j    fail
3:  li   gp, 5
    bgeu x1, x2, 4f             # 0xffffffff >= 1 (unsigned)
    j    fail
4:  li   gp, 6
    bge  x1, x1, 5f             # equal counts as >=
    j    fail
5:  li   gp, 7
    bgeu x2, x1, fail
    li   gp, 8
    blt  x1, x1, fail
    # a loop backwards: count to 10
    li   gp, 9
    li   x10, 0
    li   x11, 10
6:  addi x10, x10, 1
    blt  x10, x11, 6b
    bne  x10, x11, fail
    # jal writes the address of the next instruction
    li   gp, 10
    jal  x10, 7f
8:  j    fail
7:  la   x11, 8b
    bne  x10, x11, fail
    # jalr clears bit 0 of the target
    li   gp, 11
    la   x12, 9f + 1
    jalr x13, 0(x12)
10: j    fail
9:  la   x11, 10b
    bne  x13, x11, fail
    # jalr with rd = rs1: the target is worked out before rd changes
    li   gp, 12
    la   x14, 11f
    jalr x14, 0(x14)
12: j    fail
11: la   x11, 12b
    bne  x14, x11, fail
    # a negative offset
    li   gp, 13
    la   x12, 13f + 8
    jalr x0, -8(x12)
    j    fail
13: # pseudo-branches
    li   gp, 14
    li   x1, 0
    bnez x1, fail
    beqz x1, 14f
    j    fail
14: li   gp, 15
    li   x1, -5
    bgtz x1, fail
    blez x1, 15f
    j    fail
15: li   gp, 16
    li   x1, 3
    li   x2, 7
    bgt  x1, x2, fail
    ble  x1, x2, 16f
    j    fail
16: li   gp, 17
    li   x1, -1
    bleu x1, x2, fail
    bgtu x1, x2, 17f
    j    fail
17: # call and ret
    li   gp, 18
    li   x10, 0
    call add_five
    li   x7, 5
    bne  x10, x7, fail
    TEST_DONE

add_five:
    addi x10, x10, 5
    ret
