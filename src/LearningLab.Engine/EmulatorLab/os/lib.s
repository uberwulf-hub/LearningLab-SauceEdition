# lib.s - small helpers used all over LabOS.

.text
# strlen(a0 = text) -> a0 = number of characters before the 0 byte
strlen:
    mv   t0, a0
1:  lbu  t1, 0(t0)
    beqz t1, 2f
    addi t0, t0, 1
    j    1b
2:  sub  a0, t0, a0
    ret

# strcmp(a0, a1) -> a0 = 0 if the texts are the same, otherwise the difference of the first different character
strcmp:
1:  lbu  t0, 0(a0)
    lbu  t1, 0(a1)
    bne  t0, t1, 2f
    beqz t0, 2f
    addi a0, a0, 1
    addi a1, a1, 1
    j    1b
2:  sub  a0, t0, t1
    ret

# strcpy(a0 = to, a1 = from): copy a text including its 0 byte
strcpy:
1:  lbu  t0, 0(a1)
    sb   t0, 0(a0)
    addi a0, a0, 1
    addi a1, a1, 1
    bnez t0, 1b
    ret

# memset(a0 = address, a1 = byte, a2 = count)
memset:
    beqz a2, 2f
    add  a2, a2, a0
1:  sb   a1, 0(a0)
    addi a0, a0, 1
    bltu a0, a2, 1b
2:  ret

# memcpy(a0 = to, a1 = from, a2 = count)
memcpy:
    beqz a2, 2f
    add  a2, a2, a1
1:  lbu  t0, 0(a1)
    sb   t0, 0(a0)
    addi a0, a0, 1
    addi a1, a1, 1
    bltu a1, a2, 1b
2:  ret

# skip_spaces(a0 = text) -> a0 = the first character that isn't a space
skip_spaces:
    li   t1, ' '
1:  lbu  t0, 0(a0)
    bne  t0, t1, 2f
    addi a0, a0, 1
    j    1b
2:  ret

# split_word(a0 = text) -> a0 = the first word (now ending with a 0 byte), a1 = the rest (spaces skipped)
split_word:
    addi sp, sp, -16
    sw   ra, 12(sp)
    call skip_spaces
    mv   t2, a0                     # start of the word
    li   t1, ' '
1:  lbu  t0, 0(a0)
    beqz t0, 3f                     # end of the text: the rest is empty
    beq  t0, t1, 2f
    addi a0, a0, 1
    j    1b
2:  sb   zero, 0(a0)                # end the word here
    addi a0, a0, 1
    call skip_spaces
3:  mv   a1, a0
    mv   a0, t2
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# parse_num(a0 = text) -> a0 = the number, a1 = 1 if it was a number (decimal, or hex with 0x), else 0
parse_num:
    li   t0, 0                      # the value
    li   t1, 10                     # the base
    li   t5, 0                      # 1 = negative
    li   a1, 0
    lbu  t2, 0(a0)
    li   t3, '-'
    bne  t2, t3, 1f
    li   t5, 1
    addi a0, a0, 1
1:  lbu  t2, 0(a0)
    li   t3, '0'
    bne  t2, t3, 2f
    lbu  t2, 1(a0)
    ori  t2, t2, 0x20               # lower case
    li   t3, 'x'
    bne  t2, t3, 2f
    li   t1, 16
    addi a0, a0, 2
2:  lbu  t2, 0(a0)
    beqz t2, 6f
    li   t3, '0'
    sub  t4, t2, t3
    li   t3, 10
    bltu t4, t3, 4f                 # a digit 0-9
    ori  t2, t2, 0x20
    li   t3, 'a'
    sub  t4, t2, t3
    li   t3, 6
    bgeu t4, t3, 7f                 # not a-f: not a number
    addi t4, t4, 10
4:  bgeu t4, t1, 7f                 # a digit too big for the base
    mul  t0, t0, t1
    add  t0, t0, t4
    li   a1, 1
    addi a0, a0, 1
    j    2b
6:  mv   a0, t0
    beqz t5, 8f
    neg  a0, a0
8:  ret
7:  li   a0, 0
    li   a1, 0
    ret

# put_date(a0 = seconds since 1 January 1970): prints like "Thursday 15 October 2026, 14:03:07"
# Turning a day count into a date uses Howard Hinnant's method ("days from civil"): count in 400-year eras,
# with the year starting in March so the leap day comes last.
put_date:
    addi sp, sp, -48
    sw   ra, 44(sp)
    sw   s0, 40(sp)
    sw   s1, 36(sp)
    sw   s2, 32(sp)
    sw   s3, 28(sp)
    sw   s4, 24(sp)
    li   t0, 86400                  # seconds in a day
    divu s0, a0, t0                 # s0 = days since 1970
    remu s1, a0, t0                 # s1 = seconds today
    # the day of the week: 1 January 1970 was a Thursday
    addi t1, s0, 4
    li   t0, 7
    remu t1, t1, t0
    slli t1, t1, 2
    la   t0, day_names
    add  t0, t0, t1
    lw   a0, 0(t0)
    call puts
    li   a0, ' '
    call putc
    # z = days + 719468 (days from 1 March of year 0); era = z / 146097 (days in 400 years)
    li   t0, 719468
    add  t0, s0, t0
    li   t1, 146097
    divu s2, t0, t1                 # s2 = era
    mul  t2, s2, t1
    sub  t0, t0, t2                 # t0 = doe: day of era (0-146096)
    # yoe = (doe - doe/1460 + doe/36524 - doe/146096) / 365
    li   t1, 1460
    divu t2, t0, t1
    sub  t3, t0, t2
    li   t1, 36524
    divu t2, t0, t1
    add  t3, t3, t2
    li   t1, 146096
    divu t2, t0, t1
    sub  t3, t3, t2
    li   t1, 365
    divu t3, t3, t1                 # t3 = yoe: year of era
    li   t1, 400
    mul  s3, s2, t1
    add  s3, s3, t3                 # s3 = year (counting from March)
    # doy = doe - (365 * yoe + yoe/4 - yoe/100)
    li   t1, 365
    mul  t2, t3, t1
    srli t4, t3, 2
    add  t2, t2, t4
    li   t1, 100
    divu t4, t3, t1
    sub  t2, t2, t4
    sub  t0, t0, t2                 # t0 = doy: day of the year (from 1 March)
    # mp = (5 * doy + 2) / 153; day = doy - (153 * mp + 2) / 5 + 1
    li   t1, 5
    mul  t2, t0, t1
    addi t2, t2, 2
    li   t1, 153
    divu t2, t2, t1                 # t2 = mp (0 = March)
    li   t1, 153
    mul  t4, t2, t1
    addi t4, t4, 2
    li   t1, 5
    divu t4, t4, t1
    sub  t4, t0, t4
    addi s4, t4, 1                  # s4 = day of the month
    # month = mp < 10 ? mp + 3 : mp - 9; January and February belong to the next year
    li   t1, 10
    blt  t2, t1, 1f
    addi t2, t2, -9
    addi s3, s3, 1
    j    2f
1:  addi t2, t2, 3
2:  sw   t2, 0(sp)                  # month 1-12
    mv   a0, s4
    call put_udec
    li   a0, ' '
    call putc
    lw   t2, 0(sp)
    addi t2, t2, -1
    slli t2, t2, 2
    la   t0, month_names
    add  t0, t0, t2
    lw   a0, 0(t0)
    call puts
    li   a0, ' '
    call putc
    mv   a0, s3
    call put_udec
    la   a0, msg_comma
    call puts
    # the time of day
    li   t0, 3600
    divu a0, s1, t0
    call put_dec2
    li   a0, ':'
    call putc
    li   t0, 3600
    remu a0, s1, t0
    li   t0, 60
    divu a0, a0, t0
    call put_dec2
    li   a0, ':'
    call putc
    li   t0, 60
    remu a0, s1, t0
    call put_dec2
    lw   s4, 24(sp)
    lw   s3, 28(sp)
    lw   s2, 32(sp)
    lw   s1, 36(sp)
    lw   s0, 40(sp)
    lw   ra, 44(sp)
    addi sp, sp, 48
    ret

.data
.align 2
day_names:   .word d_sun, d_mon, d_tue, d_wed, d_thu, d_fri, d_sat
month_names: .word m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11, m12
d_sun: .asciz "Sunday"
d_mon: .asciz "Monday"
d_tue: .asciz "Tuesday"
d_wed: .asciz "Wednesday"
d_thu: .asciz "Thursday"
d_fri: .asciz "Friday"
d_sat: .asciz "Saturday"
m1:  .asciz "January"
m2:  .asciz "February"
m3:  .asciz "March"
m4:  .asciz "April"
m5:  .asciz "May"
m6:  .asciz "June"
m7:  .asciz "July"
m8:  .asciz "August"
m9:  .asciz "September"
m10: .asciz "October"
m11: .asciz "November"
m12: .asciz "December"
msg_comma: .asciz ", "
.text
