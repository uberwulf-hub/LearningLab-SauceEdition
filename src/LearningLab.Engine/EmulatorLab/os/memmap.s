# memmap.s - where everything lives in Emulator Lab's 32-bit address space.
#
# A computer finds its devices the same way it finds memory: by address. Reading or writing these addresses
# doesn't touch RAM, it talks to a device instead ("memory-mapped I/O").
#
#   0x00001000  boot ROM (4 KB, read-only)    the first instructions after power on (boot.s)
#   0x02000000  CLINT timer                   mtime (the clock) and mtimecmp (the alarm)
#   0x10000000  UART serial port              send and receive text (a part of the classic 16550 chip)
#   0x10001000  keyboard                      key presses: ASCII code + PC scancode
#   0x10002000  disk                          512-byte sectors, copied to and from RAM (DMA)
#   0x10003000  real-time clock               seconds since 1 January 1970 (local time)
#   0x10004000  power                         write 0x5555 to switch off, 0x7777 to restart
#   0x10005000  video                         screen mode and the 256-colour palette
#   0x11000000  framebuffer                   320 x 200 pixels, one byte (a palette colour) each
#   0x20000000  flash (1 MB, read-only)       LabOS and the demo programs
#   0x80000000  RAM (16 MB)                   LabOS, programs, data, stacks

.equ ROM_BASE,        0x00001000

.equ CLINT_MSIP,      0x02000000      # software interrupt (bit 0)
.equ CLINT_MTIMECMP,  0x02004000      # 64-bit alarm time: the timer interrupt fires when mtime >= mtimecmp
.equ CLINT_MTIME,     0x0200BFF8      # 64-bit time in microseconds

.equ UART_BASE,       0x10000000
.equ UART_THR,        0               # write: send a byte
.equ UART_RBR,        0               # read: the received byte
.equ UART_LSR,        5               # line status: bit 0 = byte received, bit 5 = ready to send

.equ KBD_BASE,        0x10001000
.equ KBD_STATUS,      0               # bit 0 = a key is waiting
.equ KBD_DATA,        4               # read: the next key (bits 0-7 ASCII, bits 8-15 scancode), 0 = none
.equ KBD_CTRL,        8               # bit 0 = interrupt when a key arrives

.equ DISK_BASE,       0x10002000
.equ DISK_SECTOR,     0x00            # first sector
.equ DISK_ADDR,       0x04            # RAM address
.equ DISK_COUNT,      0x08            # number of sectors
.equ DISK_CMD,        0x0C            # write 1 = read from disk into RAM, 2 = write RAM to disk
.equ DISK_STATUS,     0x10            # 0 = done, 1 = error, 2 = no disk
.equ DISK_SIZE,       0x14            # disk size in sectors
.equ DISK_CMD_READ,   1
.equ DISK_CMD_WRITE,  2

.equ RTC_BASE,        0x10003000      # word 0: seconds since 1970 (local time)

.equ POWER_BASE,      0x10004000
.equ POWER_OFF,       0x5555
.equ POWER_RESET,     0x7777

.equ VIDEO_BASE,      0x10005000
.equ VIDEO_MODE,      0               # 0 = text, 1 = graphics, 2 = graphics with text on top
.equ VIDEO_PALETTE,   0x400           # 256 words: 0x00RRGGBB

.equ FB_BASE,         0x11000000
.equ SCREEN_W,        320
.equ SCREEN_H,        200

.equ FLASH_BASE,      0x20000000
.equ FLASH_MAGIC,     0x4642414C      # "LABF": the flash header starts with this
.equ FLASH_PROGRAMS,  0x20            # program directory: 32-byte entries (name[16], offset, size)

.equ RAM_BASE,        0x80000000
.equ RAM_SIZE,        0x01000000
.equ RAM_END,         RAM_BASE + RAM_SIZE

# LabOS's own layout of the RAM
.equ KERNEL_STACK_TOP,  0x80080000    # the kernel's stack grows down from here
.equ TRAP_STACK_TOP,    0x80088000    # the trap handler's own stack
.equ DIR_BUFFER,        0x80090000    # the disk directory (4 sectors) while LabOS works on it
.equ FILE_BUFFER,       0x800A0000    # file contents for cat and write (64 KB)
.equ PROGRAM_BASE,      0x80100000    # `run` loads programs here and starts them at this address
.equ PROGRAM_MAX,       0x00010000    # programs can be 64 KB
.equ PROGRAM_STACK_TOP, RAM_END       # a program's stack starts at the very top of RAM
.equ MEMTEST_START,     0x80100000    # the memory test checks everything from here...
.equ MEMTEST_END,       RAM_END       # ...to the end of RAM
