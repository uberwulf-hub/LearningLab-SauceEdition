# bare_metal.s - a program with no operating system at all ("bare metal"). Use "Run without LabOS".
# The program is put at 0x80000000 and the processor starts it straight away. Nothing else is in memory: no
# system calls, no trap handler. To print, the program talks to the serial port itself, and at the end it
# switches the computer off by writing to the power register.

.equ UART,  0x10000000
.equ POWER, 0x10004000

.text
_start:
    li   sp, 0x80100000     # a stack, so we can call a function
    la   a0, message
    call print
    li   s0, 'A'            # the alphabet, one letter at a time
1:  mv   a0, s0
    call putc
    addi s0, s0, 1
    li   t0, 'Z'
    ble  s0, t0, 1b
    li   a0, '\n'
    call putc
    la   a0, bye
    call print
    li   t0, POWER          # switch off
    li   t1, 0x5555
    sw   t1, 0(t0)
2:  j    2b

# putc(a0): wait until the serial port is ready (line status bit 5), then send the byte
putc:
    li   t0, UART
3:  lbu  t1, 5(t0)
    andi t1, t1, 0x20
    beqz t1, 3b
    sb   a0, 0(t0)
    ret

# print(a0 = text)
print:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s1, 8(sp)
    mv   s1, a0
4:  lbu  a0, 0(s1)
    beqz a0, 5f
    call putc
    addi s1, s1, 1
    j    4b
5:  lw   s1, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

.data
message: .asciz "Hello from bare metal! No operating system here.\n"
bye:     .asciz "Switching off. Press Power to start LabOS again.\n"
