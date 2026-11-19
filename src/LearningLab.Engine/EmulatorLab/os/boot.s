# boot.s - the boot ROM.
#
# When the power comes on, the processor starts running at address 0x00001000: here. A real computer has its
# BIOS or firmware in a chip at such an address. Our boot ROM does one job: find LabOS in the flash memory, copy
# it into RAM, and jump to it.

.include "memmap.s"

.text
_start:
    li   sp, KERNEL_STACK_TOP       # a stack, so we can call functions
    la   a0, hello
    call rom_puts

    li   s0, FLASH_BASE             # the flash starts with a header
    lw   t0, 0(s0)                  # word 0: the magic number "LABF"
    li   t1, FLASH_MAGIC
    bne  t0, t1, no_system          # no header: nothing to start

    lw   t2, 4(s0)                  # word 1: where LabOS is in the flash (offset)
    lw   t3, 8(s0)                  # word 2: how many bytes
    lw   s1, 12(s0)                 # word 3: where it wants to be in RAM
    add  t2, s0, t2                 # t2 = from (flash address)
    mv   t4, s1                     # t4 = to (RAM address)
    add  t3, s1, t3                 # t3 = stop here
copy:                               # copy one word (4 bytes) at a time
    bgeu t4, t3, copied
    lw   t5, 0(t2)
    sw   t5, 0(t4)
    addi t2, t2, 4
    addi t4, t4, 4
    j    copy
copied:
    la   a0, starting
    call rom_puts
    fence.i                         # we wrote new instructions to memory: make sure they are fetched fresh
    mv   a0, s0                     # tell LabOS where the flash is
    jr   s1                         # jump to LabOS. The boot ROM's work is done.

no_system:
    la   a0, nothing
    call rom_puts
1:  wfi                             # nothing to do: rest forever
    j    1b

# rom_puts: send the text at a0 (ending with a 0 byte) to the serial port
rom_puts:
    li   t0, UART_BASE
1:  lbu  t1, 0(a0)
    beqz t1, 2f
3:  lbu  t2, UART_LSR(t0)           # wait until the serial port can take a byte
    andi t2, t2, 0x20
    beqz t2, 3b
    sb   t1, UART_THR(t0)
    addi a0, a0, 1
    j    1b
2:  ret

.data
hello:    .asciz "Emulator Lab boot ROM v1.0\n"
starting: .asciz "Starting LabOS from flash...\n"
nothing:  .asciz "No operating system found in the flash memory.\n"
