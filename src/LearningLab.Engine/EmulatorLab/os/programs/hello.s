# hello.s - the smallest LabOS program: print a message and stop.
# Try it with:  run hello

.include "_syscalls.s"

.text
_start:
    la   a0, message        # a0 = the address of the text
    li   a7, SYS_PRINT      # a7 = which service: print
    ecall                   # ask LabOS

    li   a0, 0              # exit code 0 means "all went well"
    li   a7, SYS_EXIT
    ecall

.data
message:
    .asciz "Hello! This message was printed by a RISC-V program, with a little help from LabOS.\n"
