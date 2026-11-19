# _syscalls.s - LabOS's system calls, for programs. Include it with:  .include "_syscalls.s"
#
# To ask LabOS for something, put the service number in a7, the details in a0-a4, and run ecall.
# The answer (if any) comes back in a0. All other registers are kept.
#
# A program starts at its first instruction with a0 = the text typed after its name (for example "demo" in
# "run snake demo"). It ends with SYS_EXIT, or with ret.

.equ SYS_EXIT,        1     # exit(a0 = code)
.equ SYS_PUTCHAR,     2     # print one character (a0)
.equ SYS_PRINT,       3     # print text (a0 = address, ends with a 0 byte)
.equ SYS_PRINT_NUM,   4     # print a0 as a decimal number
.equ SYS_GETKEY,      5     # wait for a key -> a0 (bits 0-7 ASCII, bits 8-15 scancode)
.equ SYS_POLLKEY,     6     # a key if one was pressed, else 0 (doesn't wait)
.equ SYS_READLINE,    7     # read a line: a0 = buffer, a1 = size -> a0 = length
.equ SYS_SLEEP,       8     # wait a0 milliseconds
.equ SYS_TIME_MS,     9     # -> a0 = milliseconds since LabOS started
.equ SYS_RANDOM,      10    # -> a0 = a random 32-bit number
.equ SYS_SCREEN_MODE, 11    # a0: 0 = text, 1 = graphics, 2 = graphics with text on top
.equ SYS_CLEAR_GFX,   12    # fill the screen with colour a0
.equ SYS_PIXEL,       13    # a0 = x (0-319), a1 = y (0-199), a2 = colour (0-255)
.equ SYS_RECT,        14    # a0 = x, a1 = y, a2 = width, a3 = height, a4 = colour
.equ SYS_PALETTE,     15    # a0 = colour number, a1 = 0x00RRGGBB
.equ SYS_PRINT_HEX,   16    # print a0 as 0x and 8 hex digits
.equ SYS_SET_COLOR,   17    # text colour a0, background a1 (0-15)
.equ SYS_CLEAR_TEXT,  18    # clear the text screen
.equ SYS_GET_PIXEL,   19    # a0 = x, a1 = y -> a0 = colour

# scancodes of some keys (bits 8-15 of a key)
.equ SCAN_UP,     0x48
.equ SCAN_DOWN,   0x50
.equ SCAN_LEFT,   0x4B
.equ SCAN_RIGHT,  0x4D
.equ SCAN_ESC,    0x01
.equ SCAN_ENTER,  0x1C
.equ SCAN_SPACE,  0x39
