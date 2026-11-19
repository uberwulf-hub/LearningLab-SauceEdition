# guess.s - guess the number. The computer picks a number from 1 to 100; you guess, it says higher or lower.
# Try it with:  run guess
#
# Shows reading a line of text, turning digits into a number, comparing, and counting.

.include "_syscalls.s"

.text
_start:
    la   a0, intro
    li   a7, SYS_PRINT
    ecall

new_game:
    li   a7, SYS_RANDOM
    ecall
    li   t0, 100
    remu s0, a0, t0         # s0 = the secret number, 1-100
    addi s0, s0, 1
    li   s1, 0              # s1 = guesses so far
    la   a0, thinking
    li   a7, SYS_PRINT
    ecall

ask:
    la   a0, prompt
    li   a7, SYS_PRINT
    ecall
    la   a0, buffer
    li   a1, 16
    li   a7, SYS_READLINE
    ecall
    beqz a0, ask            # nothing typed
    la   a0, buffer
    lbu  t0, 0(a0)
    ori  t0, t0, 0x20
    li   t1, 'q'
    beq  t0, t1, bye        # q stops
    call to_number          # a0 = the number, a1 = 1 if it was one
    beqz a1, not_a_number
    li   t0, 1
    blt  a0, t0, not_a_number
    li   t0, 100
    bgt  a0, t0, not_a_number
    addi s1, s1, 1
    blt  a0, s0, too_low
    bgt  a0, s0, too_high
    # right!
    la   a0, right1
    li   a7, SYS_PRINT
    ecall
    mv   a0, s1
    li   a7, SYS_PRINT_NUM
    ecall
    la   a0, right2
    li   t0, 1
    bne  s1, t0, 1f
    la   a0, right2one
1:  li   a7, SYS_PRINT
    ecall
    la   a0, again
    li   a7, SYS_PRINT
    ecall
    la   a0, buffer
    li   a1, 16
    li   a7, SYS_READLINE
    ecall
    la   a0, buffer
    lbu  t0, 0(a0)
    ori  t0, t0, 0x20
    li   t1, 'y'
    beq  t0, t1, new_game
bye:
    la   a0, goodbye
    li   a7, SYS_PRINT
    ecall
    li   a0, 0
    li   a7, SYS_EXIT
    ecall

too_low:
    la   a0, low
    j    say
too_high:
    la   a0, high
    j    say
not_a_number:
    la   a0, notnum
say:
    li   a7, SYS_PRINT
    ecall
    j    ask

# to_number(a0 = text) -> a0 = value, a1 = 1 if the text was all digits
# Each digit: value = value * 10 + digit. "42" -> 0*10+4 = 4 -> 4*10+2 = 42.
to_number:
    li   t0, 0              # value
    li   a1, 0
    li   t3, 10
1:  lbu  t1, 0(a0)
    beqz t1, 2f
    addi t1, t1, -'0'
    bgeu t1, t3, 3f         # not 0-9
    mul  t0, t0, t3
    add  t0, t0, t1
    li   a1, 1
    addi a0, a0, 1
    j    1b
3:  li   a1, 0
2:  mv   a0, t0
    ret

.data
intro:     .asciz "\n\e[93mGUESS THE NUMBER\e[0m\n"
thinking:  .asciz "I'm thinking of a number from 1 to 100. Can you guess it? (q stops)\n"
prompt:    .asciz "Your guess: "
low:       .asciz "Too low! Try a bigger number.\n"
high:      .asciz "Too high! Try a smaller number.\n"
notnum:    .asciz "Please type a number from 1 to 100.\n"
right1:    .asciz "\e[92mYou got it in "
right2:    .asciz " guesses!\e[0m\n"
right2one: .asciz " guess! Amazing!\e[0m\n"
again:     .asciz "Play again? (y/n) "
goodbye:   .asciz "Thanks for playing!\n"
buffer:    .space 16
