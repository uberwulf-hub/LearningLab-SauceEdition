# shell.s - the command line. It prints a prompt, reads a line, finds the command in a table and calls it.
# Each command gets a0 = the rest of the line (its arguments).

.equ LINE_MAX, 78

.text
shell:
    li   sp, KERNEL_STACK_TOP       # the shell starts with a fresh stack
shell_loop:
    li   a0, 10                     # the prompt in light green
    li   a1, 0
    call set_color
    la   a0, prompt
    call puts
    call default_color
    la   a0, line
    li   a1, LINE_MAX
    call readline
    la   a0, line
    call split_word                 # a0 = the command, a1 = its arguments
    lbu  t0, 0(a0)
    beqz t0, shell_loop             # an empty line
    mv   s1, a1
    mv   s2, a0
    la   s0, commands               # look for it in the table
1:  lw   a1, 0(s0)                  # the command's name
    beqz a1, 2f                     # end of the table
    mv   a0, s2
    call strcmp
    beqz a0, 3f
    addi s0, s0, 12                 # next entry: name, function, help text
    j    1b
2:  la   a0, msg_unknown
    call puts
    mv   a0, s2
    call puts
    la   a0, msg_unknown2
    call puts
    j    shell_loop
3:  lw   t0, 4(s0)                  # the command's function
    mv   a0, s1
    jalr t0
    j    shell_loop

# ---------------------------------------------------------------------------------------------------------------
cmd_help:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    la   a0, msg_help
    call puts
    la   s0, commands
1:  lw   t0, 0(s0)
    beqz t0, 2f
    li   a0, 2
    call put_spaces
    li   a0, 14                     # yellow command name
    li   a1, 0
    call set_color
    lw   a0, 0(s0)
    call puts
    call default_color
    lw   a0, 0(s0)
    call strlen
    li   t0, 9
    sub  a0, t0, a0
    call put_spaces
    lw   a0, 8(s0)                  # what it does
    call puts
    call newline
    addi s0, s0, 12
    j    1b
2:  la   a0, msg_help2
    call puts
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

cmd_clear:
    j    clear_screen

cmd_echo:
    addi sp, sp, -16
    sw   ra, 12(sp)
    call puts
    call newline
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# mem: the memory map and how LabOS uses the RAM
cmd_mem:
    addi sp, sp, -16
    sw   ra, 12(sp)
    la   a0, msg_mem1
    call puts
    la   a0, memory_ok
    lw   t0, 0(a0)
    la   a0, msg_mem_ok
    bnez t0, 1f
    la   a0, msg_mem_bad
1:  call puts
    la   a0, msg_mem2
    call puts
    la   a0, kernel_end
    li   t0, RAM_BASE
    sub  a0, a0, t0
    call put_udec
    la   a0, msg_mem3
    call puts
    li   t0, PROGRAM_STACK_TOP
    li   t1, PROGRAM_BASE
    sub  a0, t0, t1
    srli a0, a0, 10
    call put_udec
    la   a0, msg_mem4
    call puts
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# clock: how long LabOS has been running, and the time
cmd_clock:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    la   a0, msg_uptime
    call puts
    la   t0, ticks
    lw   t0, 0(t0)
    li   t1, 100
    divu s0, t0, t1                 # seconds since start
    li   t1, 3600
    divu a0, s0, t1
    call put_udec
    li   a0, ':'
    call putc
    li   t1, 3600
    remu a0, s0, t1
    li   t1, 60
    divu a0, a0, t1
    call put_dec2
    li   a0, ':'
    call putc
    li   t1, 60
    remu a0, s0, t1
    call put_dec2
    la   a0, msg_ticks
    call puts
    la   t0, ticks
    lw   a0, 0(t0)
    call put_udec
    la   a0, msg_ticks2
    call puts
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    # and the date and time: fall through to cmd_date

# date: today's date and time from the real-time clock
cmd_date:
    addi sp, sp, -16
    sw   ra, 12(sp)
    la   a0, msg_date
    call puts
    li   t0, RTC_BASE
    lw   a0, 0(t0)                  # seconds since 1970
    call put_date
    call newline
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# ls: files on the disk, then the programs built into the flash
cmd_ls:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    la   t0, fs_ok
    lw   t0, 0(t0)
    beqz t0, 2f
    la   a0, msg_ls_disk
    call puts
    call fs_list
    bnez a0, 2f
    la   a0, msg_ls_none
    call puts
2:  la   a0, msg_ls_flash
    call puts
    li   s0, FLASH_BASE
    lw   s1, 16(s0)                 # how many programs
    addi s0, s0, FLASH_PROGRAMS
3:  beqz s1, 4f
    li   a0, 2
    call put_spaces
    mv   a0, s0
    call puts                       # its name
    mv   a0, s0
    call strlen
    li   t0, 26
    sub  a0, t0, a0
    call put_spaces
    lw   a0, 20(s0)                 # its size
    call put_udec
    la   a0, msg_bytes
    call puts
    addi s0, s0, 32
    addi s1, s1, -1
    j    3b
4:  la   a0, msg_ls_hint
    call puts
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# need_disk -> a0 = 1 if the file system is ready (otherwise says so)
need_disk:
    la   t0, fs_ok
    lw   a0, 0(t0)
    bnez a0, 1f
    addi sp, sp, -16
    sw   ra, 12(sp)
    la   a0, msg_nodisk
    call puts
    lw   ra, 12(sp)
    addi sp, sp, 16
    li   a0, 0
1:  ret

# need_name(a0 = arguments) -> a0 = the first word (the file name), a1 = the rest; a0 = 0 if there is none
need_name:
    addi sp, sp, -16
    sw   ra, 12(sp)
    call split_word
    lbu  t0, 0(a0)
    bnez t0, 1f
    la   a0, msg_need_name
    call puts
    li   a0, 0
1:  lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# cat <file>: show what is in a file
cmd_cat:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    call need_name
    beqz a0, 9f
    mv   s0, a0
    call need_disk
    beqz a0, 9f
    mv   a0, s0
    li   a1, FILE_BUFFER
    call fs_read
    bltz a0, 8f
    li   s0, FILE_BUFFER            # print each byte; anything that isn't text shows as a dot
    add  s1, s0, a0
1:  bgeu s0, s1, 2f
    lbu  a0, 0(s0)
    li   t0, 10
    beq  a0, t0, 3f
    li   t0, 32
    blt  a0, t0, 4f
    li   t0, 127
    blt  a0, t0, 3f
4:  li   a0, '.'
3:  call putc
    addi s0, s0, 1
    j    1b
2:  li   t0, FILE_BUFFER            # end with a new line if the file didn't
    beq  s1, t0, 9f
    lbu  t0, -1(s1)
    li   t1, 10
    beq  t0, t1, 9f
    call newline
    j    9f
8:  mv   a0, s0
    call no_such_file
9:  lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

no_such_file:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   a0, 8(sp)
    la   a0, msg_nofile
    call puts
    lw   a0, 8(sp)
    call puts
    la   a0, msg_nofile2
    call puts
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# write <file> <text>: save the text as a file. Without text, read lines until a line with just a dot.
cmd_write:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    sw   s2, 0(sp)
    call need_name
    beqz a0, 9f
    mv   s0, a0                     # s0 = name
    mv   s2, a1                     # s2 = the text after it
    call need_disk
    beqz a0, 9f
    li   s1, FILE_BUFFER            # s1 = where the next text goes
    lbu  t0, 0(s2)
    beqz t0, 2f
    mv   a0, s1                     # one line, given after the name
    mv   a1, s2
    call strcpy
    mv   a0, s1
    call strlen
    add  s1, s1, a0
    li   t0, 10
    sb   t0, 0(s1)
    addi s1, s1, 1
    j    5f
2:  la   a0, msg_write_more
    call puts
3:  la   a0, msg_more_prompt
    call puts
    mv   a0, s1
    li   a1, LINE_MAX
    call readline
    lbu  t0, 0(s1)                  # a line with just "." ends the file
    li   t1, '.'
    bne  t0, t1, 4f
    lbu  t0, 1(s1)
    beqz t0, 5f
4:  add  s1, s1, a0
    li   t0, 10
    sb   t0, 0(s1)
    addi s1, s1, 1
    li   t0, FILE_BUFFER + 60000    # keep it a sensible size
    bltu s1, t0, 3b
5:  mv   a0, s0
    li   a1, FILE_BUFFER
    li   t0, FILE_BUFFER
    sub  a2, s1, t0
    call fs_write
    beqz a0, 6f
    la   a0, msg_write_fail
    call puts
    j    9f
6:  la   a0, msg_saved
    call puts
    mv   a0, s0
    call puts
    call newline
9:  lw   s2, 0(sp)
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# rm <file>: delete a file
cmd_rm:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    call need_name
    beqz a0, 9f
    mv   s0, a0
    call need_disk
    beqz a0, 9f
    mv   a0, s0
    call fs_delete
    beqz a0, 1f
    mv   a0, s0
    call no_such_file
    j    9f
1:  la   a0, msg_removed
    call puts
    mv   a0, s0
    call puts
    call newline
9:  lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# color <text> [background]: colours by number (0-15) or name
cmd_color:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    call split_word
    mv   s1, a1                     # the background, if any
    lbu  t0, 0(a0)
    beqz t0, 7f
    call color_number
    bltz a0, 8f
    mv   s0, a0
    lbu  t0, 0(s1)
    li   a0, 0
    beqz t0, 1f
    mv   a0, s1
    call split_word
    call color_number
    bltz a0, 8f
1:  beq  a0, s0, 6f                 # the same colour twice: you couldn't read anything
    la   t0, text_bg
    sw   a0, 0(t0)
    la   t0, text_fg
    sw   s0, 0(t0)
    call default_color
    la   a0, msg_color_ok
    call puts
    j    9f
6:  la   a0, msg_color_same
    call puts
    j    9f
7:  la   a0, msg_color_help         # no colour given: show them
    call puts
    li   s0, 0
2:  li   t0, 16
    bge  s0, t0, 3f
    mv   a0, s0
    li   a1, 0
    beqz s0, 4f
    j    5f
4:  li   a1, 7                      # black on grey, so it shows
5:  call set_color
    la   t0, color_names
    slli t1, s0, 2
    add  t0, t0, t1
    lw   a0, 0(t0)
    call puts
    call default_color
    li   a0, ' '
    call putc
    addi s0, s0, 1
    j    2b
3:  call newline
    j    9f
8:  la   a0, msg_color_bad
    call puts
9:  lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# color_number(a0 = text) -> a0 = 0-15, or -1
color_number:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    mv   s0, a0
    call parse_num
    beqz a1, 1f
    li   t0, 16
    bgeu a0, t0, 3f
    j    4f
1:  li   s1, 0                      # look the name up
2:  li   t0, 16
    bge  s1, t0, 3f
    la   t0, color_names
    slli t1, s1, 2
    add  t0, t0, t1
    lw   a1, 0(t0)
    mv   a0, s0
    call strcmp
    beqz a0, 5f
    addi s1, s1, 1
    j    2b
5:  mv   a0, s1
    j    4f
3:  li   a0, -1
4:  lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# peek <address>: show 64 bytes of memory in hex and as text
cmd_peek:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    sw   s2, 0(sp)
    call parse_num
    beqz a1, 8f
    andi s0, a0, -16                # start on a 16-byte boundary
    # only RAM, flash and the boot ROM: device registers can change when they are read, and reading an
    # address with nothing there would crash LabOS
    li   t0, RAM_BASE
    bltu s0, t0, 10f
    li   t0, RAM_END - 64
    bgtu s0, t0, 8f
    j    1f
10: li   t0, FLASH_BASE
    bltu s0, t0, 7f
    li   t0, FLASH_BASE + 0x100000 - 64
    bgtu s0, t0, 8f
    j    1f
7:  li   t0, ROM_BASE
    bltu s0, t0, 8f
    li   t0, ROM_BASE + 0x1000 - 64
    bgtu s0, t0, 8f
1:  li   s1, 4                      # 4 rows of 16 bytes
2:  mv   a0, s0
    call put_hex
    li   a0, ':'
    call putc
    li   s2, 0
3:  li   a0, ' '
    call putc
    add  t0, s0, s2
    lbu  a0, 0(t0)
    call put_hex2
    addi s2, s2, 1
    li   t0, 16
    blt  s2, t0, 3b
    li   a0, 2
    call put_spaces
    li   s2, 0
4:  add  t0, s0, s2
    lbu  a0, 0(t0)
    li   t1, 32
    blt  a0, t1, 5f
    li   t1, 127
    blt  a0, t1, 6f
5:  li   a0, '.'
6:  call putc
    addi s2, s2, 1
    li   t0, 16
    blt  s2, t0, 4b
    call newline
    addi s0, s0, 16
    addi s1, s1, -1
    bnez s1, 2b
    j    9f
8:  la   a0, msg_peek_help
    call puts
9:  lw   s2, 0(sp)
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

cmd_reboot:
    la   a0, msg_reboot
    call puts
    li   t0, POWER_BASE
    li   t1, POWER_RESET
    sw   t1, 0(t0)
1:  j    1b

cmd_halt:
    la   a0, msg_halt
    call puts
    li   t0, POWER_BASE
    li   t1, POWER_OFF
    sw   t1, 0(t0)
1:  j    1b

# ---------------------------------------------------------------------------------------------------------------
# run <program> [arguments]: load a program into RAM at PROGRAM_BASE and start it.
#
# The program gets its own stack at the top of RAM, a0 = its arguments, and ra = program_return, so a plain
# `ret` from the program ends it. Ending with the exit system call (or crashing) also arrives at program_return,
# via the trap handler.
cmd_run:
    addi sp, sp, -64
    sw   ra, 60(sp)                 # save everything the program might change: it may not follow the rules
    sw   s0, 56(sp)
    sw   s1, 52(sp)
    sw   s2, 48(sp)
    sw   s3, 44(sp)
    sw   s4, 40(sp)
    sw   s5, 36(sp)
    sw   s6, 32(sp)
    sw   s7, 28(sp)
    sw   s8, 24(sp)
    sw   s9, 20(sp)
    sw   s10, 16(sp)
    sw   s11, 12(sp)
    call need_name
    beqz a0, run_end
    mv   s0, a0                     # name
    mv   s1, a1                     # its arguments
    # first the disk: your own programs can replace the built-in ones
    la   t0, fs_ok
    lw   t0, 0(t0)
    beqz t0, 1f
    mv   a0, s0
    call fs_find
    bltz a0, 1f
    mv   a0, s0
    li   a1, PROGRAM_BASE
    call fs_read
    bgez a0, 3f
1:  # then the flash
    li   s2, FLASH_BASE
    lw   s3, 16(s2)                 # number of programs
    addi s2, s2, FLASH_PROGRAMS
2:  beqz s3, run_not_found
    mv   a0, s0
    mv   a1, s2
    call strcmp
    beqz a0, 4f
    addi s2, s2, 32
    addi s3, s3, -1
    j    2b
4:  li   a0, PROGRAM_BASE           # copy it out of the flash
    lw   t0, 16(s2)
    li   a1, FLASH_BASE
    add  a1, a1, t0
    lw   a2, 20(s2)
    call memcpy
3:  fence.i                         # new code in memory
    la   t0, program_running
    li   t1, 1
    sw   t1, 0(t0)
    la   t0, break_requested
    sw   zero, 0(t0)
    la   t0, shell_sp
    sw   sp, 0(t0)                  # where our stack is, to come back to
    mv   a0, s1
    la   ra, program_return
    li   sp, PROGRAM_STACK_TOP
    li   t0, PROGRAM_BASE
    jr   t0                         # off it goes

# program_return(a0 = exit code): every way a program ends comes here
program_return:
    la   t0, shell_sp
    lw   sp, 0(t0)
    mv   s0, a0
    la   t0, program_running
    sw   zero, 0(t0)
    li   t0, KBD_BASE               # keyboard interrupts on (in case the program switched them off)
    li   t1, 1
    sw   t1, KBD_CTRL(t0)
    csrsi mstatus, 8                # and interrupts in general
    call default_color
    li   t0, VIDEO_BASE             # back to the text screen (a fresh one if the program used graphics)
    lw   t1, VIDEO_MODE(t0)
    sw   zero, VIDEO_MODE(t0)
    beqz t1, 1f
    call clear_screen
1:
    beqz s0, run_end
    la   a0, msg_exit_code
    call puts
    mv   a0, s0
    call put_dec
    li   a0, ')'
    call putc
    call newline
    j    run_end
run_not_found:
    la   a0, msg_noprog
    call puts
    mv   a0, s0
    call puts
    la   a0, msg_noprog2
    call puts
run_end:
    lw   s11, 12(sp)
    lw   s10, 16(sp)
    lw   s9, 20(sp)
    lw   s8, 24(sp)
    lw   s7, 28(sp)
    lw   s6, 32(sp)
    lw   s5, 36(sp)
    lw   s4, 40(sp)
    lw   s3, 44(sp)
    lw   s2, 48(sp)
    lw   s1, 52(sp)
    lw   s0, 56(sp)
    lw   ra, 60(sp)
    addi sp, sp, 64
    ret

# ---------------------------------------------------------------------------------------------------------------
.data
.align 2
commands:   # name, function, what it does
    .word n_help,   cmd_help,   h_help
    .word n_clear,  cmd_clear,  h_clear
    .word n_echo,   cmd_echo,   h_echo
    .word n_ls,     cmd_ls,     h_ls
    .word n_cat,    cmd_cat,    h_cat
    .word n_write,  cmd_write,  h_write
    .word n_rm,     cmd_rm,     h_rm
    .word n_run,    cmd_run,    h_run
    .word n_mem,    cmd_mem,    h_mem
    .word n_peek,   cmd_peek,   h_peek
    .word n_clock,  cmd_clock,  h_clock
    .word n_date,   cmd_date,   h_date
    .word n_color,  cmd_color,  h_color
    .word n_reboot, cmd_reboot, h_reboot
    .word n_halt,   cmd_halt,   h_halt
    .word 0
shell_sp:   .word 0

n_help:   .asciz "help"
n_clear:  .asciz "clear"
n_echo:   .asciz "echo"
n_ls:     .asciz "ls"
n_cat:    .asciz "cat"
n_write:  .asciz "write"
n_rm:     .asciz "rm"
n_run:    .asciz "run"
n_mem:    .asciz "mem"
n_peek:   .asciz "peek"
n_clock:  .asciz "clock"
n_date:   .asciz "date"
n_color:  .asciz "color"
n_reboot: .asciz "reboot"
n_halt:   .asciz "halt"
h_help:   .asciz "this list"
h_clear:  .asciz "clear the screen"
h_echo:   .asciz "print some text:  echo Hello!"
h_ls:     .asciz "list the files and programs"
h_cat:    .asciz "show a file:  cat notes"
h_write:  .asciz "save text in a file:  write notes Buy milk"
h_rm:     .asciz "delete a file:  rm notes"
h_run:    .asciz "start a program:  run snake"
h_mem:    .asciz "how the memory is used"
h_peek:   .asciz "look at memory:  peek 0x80000000"
h_clock:  .asciz "how long LabOS has been running"
h_date:   .asciz "today's date and time"
h_color:  .asciz "change the colours:  color yellow blue"
h_reboot: .asciz "restart the computer"
h_halt:   .asciz "switch the computer off"

.align 2                            # a table of words must start on a 4-byte boundary
color_names:
    .word cn0, cn1, cn2, cn3, cn4, cn5, cn6, cn7, cn8, cn9, cn10, cn11, cn12, cn13, cn14, cn15
cn0:  .asciz "black"
cn1:  .asciz "blue"
cn2:  .asciz "green"
cn3:  .asciz "cyan"
cn4:  .asciz "red"
cn5:  .asciz "magenta"
cn6:  .asciz "brown"
cn7:  .asciz "grey"
cn8:  .asciz "darkgrey"
cn9:  .asciz "lightblue"
cn10: .asciz "lightgreen"
cn11: .asciz "lightcyan"
cn12: .asciz "pink"
cn13: .asciz "lightmagenta"
cn14: .asciz "yellow"
cn15: .asciz "white"

prompt:          .asciz "lab> "
line:            .space 80
msg_unknown:     .asciz "I don't know the command \""
msg_unknown2:    .asciz "\". Type help to see the commands.\n"
msg_help:        .asciz "Commands:\n"
msg_help2:       .asciz "Keys: Up and Down bring back earlier lines, Esc clears the line, Ctrl+C stops a program.\n"
msg_mem1:        .asciz "Memory map:\n  0x00001000  boot ROM (4 KB)\n  0x02000000  timer (CLINT)\n  0x10000000  serial port, keyboard, disk, clock, power, video\n  0x11000000  screen (320 x 200 pixels)\n  0x20000000  flash: LabOS and the built-in programs (1 MB)\n  0x80000000  RAM: 16 MB, "
msg_mem_ok:      .asciz "tested OK at start-up\n"
msg_mem_bad:     .asciz "the memory test FAILED\n"
msg_mem2:        .asciz "RAM use:\n  0x80000000  LabOS (code and data: "
msg_mem3:        .asciz " bytes)\n  0x80080000  LabOS's stack and trap stack\n  0x80090000  disk buffers\n  0x80100000  programs (loaded by run) and their data, "
msg_mem4:        .asciz " KB\n  0x81000000  (end of RAM; a program's stack grows down from here)\n"
msg_uptime:      .asciz "LabOS has been running for "
msg_ticks:       .asciz " ("
msg_ticks2:      .asciz " timer ticks).\n"
msg_date:        .asciz "It is "
msg_ls_disk:     .asciz "Files on the disk:\n"
msg_ls_none:     .asciz "  (none yet: try  write hello Hi there)\n"
msg_ls_flash:    .asciz "Built-in programs:\n"
msg_ls_hint:     .asciz "Start a program with run, like:  run bounce\n"
msg_need_name:   .asciz "Which file? Put its name after the command.\n"
msg_nofile:      .asciz "There is no file called \""
msg_nofile2:     .asciz "\". Type ls to see the files.\n"
msg_write_more:  .asciz "Type the text. Finish with a line with just a dot (.) on it.\n"
msg_more_prompt: .asciz "> "
msg_write_fail:  .asciz "The file could not be saved (is the disk full, or the name too long?).\n"
msg_saved:       .asciz "Saved "
msg_removed:     .asciz "Deleted "
msg_color_ok:    .asciz "New colours.\n"
msg_color_same:  .asciz "Text and background can't be the same colour.\n"
msg_color_bad:   .asciz "Colours are numbers 0-15 or names. Type color to see them.\n"
msg_color_help:  .asciz "Use: color <text> [background]. The colours:\n"
msg_peek_help:   .asciz "Use: peek <address>, like  peek 0x80000000  (RAM, flash or the boot ROM).\n"
msg_reboot:      .asciz "Restarting...\n"
msg_halt:        .asciz "Goodbye! Switching off.\n"
msg_noprog:      .asciz "There is no program called \""
msg_noprog2:     .asciz "\". Type ls to see them.\n"
msg_exit_code:   .asciz "(the program ended with code "
.text
