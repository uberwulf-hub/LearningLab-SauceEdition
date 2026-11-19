# disk.s - the disk driver and LabFS, a tiny flat file system.
#
# The disk is a row of 512-byte sectors. To read, LabOS tells the disk which sector, how many, and where in RAM
# to put them, then gives the command; the disk copies them straight into memory ("DMA", direct memory access).
#
# LabFS layout:
#   sector 0       the superblock: the magic number "LBFS", version, number of file slots, slot size
#   sectors 1-4    the directory: 64 entries of 32 bytes
#                    bytes 0-23  the name (ending with a 0 byte)
#                    bytes 24-27 the size in bytes
#                    bytes 28-31 flags (bit 0 = this entry is in use)
#   sector 8...    the files: file number i lives in the 128 sectors (64 KB) starting at sector 8 + i * 128
# Simple and easy to follow; a real file system would share the space out more cleverly.

.equ FS_MAGIC,        0x5346424C    # "LBFS"
.equ FS_SLOTS,        64
.equ FS_SLOT_SECTORS, 128
.equ FS_DATA_START,   8
.equ FS_ENTRY,        32
.equ FS_NAME_MAX,     23

.text
# disk_io(a0 = first sector, a1 = RAM address, a2 = count, a3 = command) -> a0 = status (0 = OK)
disk_io:
    li   t0, DISK_BASE
    sw   a0, DISK_SECTOR(t0)
    sw   a1, DISK_ADDR(t0)
    sw   a2, DISK_COUNT(t0)
    sw   a3, DISK_CMD(t0)           # the copy happens now
    lw   a0, DISK_STATUS(t0)
    ret

disk_read:
    li   a3, DISK_CMD_READ
    j    disk_io
disk_write:
    li   a3, DISK_CMD_WRITE
    j    disk_io

# fs_init: check the disk; format it if it has never been used
fs_init:
    addi sp, sp, -16
    sw   ra, 12(sp)
    li   t0, DISK_BASE
    lw   t1, DISK_SIZE(t0)
    li   t2, FS_DATA_START + FS_SLOTS * FS_SLOT_SECTORS
    bgeu t1, t2, 1f
    la   a0, msg_nodisk
    call puts
    j    3f
1:  li   a0, 0
    li   a1, DIR_BUFFER
    li   a2, 1
    call disk_read
    bnez a0, 2f
    li   t0, DIR_BUFFER
    lw   t1, 0(t0)
    li   t2, FS_MAGIC
    beq  t1, t2, 4f
2:  la   a0, msg_formatting
    call puts
    call fs_format
    la   a0, msg_formatted
    call puts
4:  li   t0, 1
    la   t1, fs_ok
    sw   t0, 0(t1)
3:  lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# fs_format: a new, empty superblock and directory
fs_format:
    addi sp, sp, -16
    sw   ra, 12(sp)
    li   a0, DIR_BUFFER
    li   a1, 0
    li   a2, 5 * 512
    call memset
    li   t0, DIR_BUFFER
    li   t1, FS_MAGIC
    sw   t1, 0(t0)
    li   t1, 1                      # version
    sw   t1, 4(t0)
    li   t1, FS_SLOTS
    sw   t1, 8(t0)
    li   t1, FS_SLOT_SECTORS
    sw   t1, 12(t0)
    li   t1, FS_DATA_START
    sw   t1, 16(t0)
    li   t1, 4
    sw   t1, 20(t0)
    li   a0, 0
    li   a1, DIR_BUFFER
    li   a2, 5
    call disk_write
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# fs_load_dir / fs_save_dir: the directory sectors to and from DIR_BUFFER
fs_load_dir:
    li   a0, 1
    li   a1, DIR_BUFFER
    li   a2, 4
    j    disk_read
fs_save_dir:
    li   a0, 1
    li   a1, DIR_BUFFER
    li   a2, 4
    j    disk_write

# fs_find(a0 = name) -> a0 = entry number, or -1. Loads the directory first.
fs_find:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    mv   s0, a0
    call fs_load_dir
    li   s1, 0
1:  li   t0, FS_SLOTS
    bge  s1, t0, 3f
    li   t0, FS_ENTRY
    mul  t0, s1, t0
    li   t1, DIR_BUFFER
    add  a1, t1, t0                 # a1 = this entry
    lw   t2, 28(a1)
    andi t2, t2, 1
    beqz t2, 2f                     # empty slot
    mv   a0, s0
    call strcmp
    beqz a0, 4f
2:  addi s1, s1, 1
    j    1b
3:  li   s1, -1
4:  mv   a0, s1
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# fs_list: print every file with its size; returns a0 = number of files
fs_list:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    call fs_load_dir
    li   s0, 0                      # entry number
    li   s1, 0                      # files found
1:  li   t0, FS_SLOTS
    bge  s0, t0, 3f
    li   t0, FS_ENTRY
    mul  t0, s0, t0
    li   t1, DIR_BUFFER
    add  t1, t1, t0
    lw   t2, 28(t1)
    andi t2, t2, 1
    beqz t2, 2f
    sw   t1, 0(sp)
    li   a0, 2
    call put_spaces
    lw   a0, 0(sp)
    call puts                       # the name
    lw   a0, 0(sp)
    call strlen
    li   t0, 26
    sub  a0, t0, a0
    call put_spaces
    lw   t1, 0(sp)
    lw   a0, 24(t1)
    call put_udec                   # the size
    la   a0, msg_bytes
    call puts
    addi s1, s1, 1
2:  addi s0, s0, 1
    j    1b
3:  mv   a0, s1
    lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# fs_read(a0 = name, a1 = where to put it) -> a0 = size in bytes, or -1 if there is no such file
fs_read:
    addi sp, sp, -16
    sw   ra, 12(sp)
    sw   s0, 8(sp)
    sw   s1, 4(sp)
    mv   s1, a1
    call fs_find
    bltz a0, 1f
    mv   s0, a0                     # entry number
    li   t0, FS_ENTRY
    mul  t0, s0, t0
    li   t1, DIR_BUFFER
    add  t1, t1, t0
    lw   t2, 24(t1)                 # size
    sw   t2, 0(sp)
    addi a2, t2, 511
    srli a2, a2, 9                  # sectors = (size + 511) / 512
    beqz a2, 2f
    li   t0, FS_SLOT_SECTORS
    mul  a0, s0, t0
    addi a0, a0, FS_DATA_START      # first sector of this file
    mv   a1, s1
    call disk_read
2:  lw   a0, 0(sp)
1:  lw   s1, 4(sp)
    lw   s0, 8(sp)
    lw   ra, 12(sp)
    addi sp, sp, 16
    ret

# fs_write(a0 = name, a1 = data, a2 = size) -> a0 = 0 OK, -1 the disk is full, -2 too big, -3 bad name
fs_write:
    addi sp, sp, -32
    sw   ra, 28(sp)
    sw   s0, 24(sp)
    sw   s1, 20(sp)
    sw   s2, 16(sp)
    sw   s3, 12(sp)
    mv   s0, a0
    mv   s1, a1
    mv   s2, a2
    li   t0, FS_SLOT_SECTORS * 512
    li   a0, -2
    bgtu s2, t0, 9f
    mv   a0, s0
    call strlen
    li   t0, FS_NAME_MAX
    beqz a0, 8f
    bgt  a0, t0, 8f
    mv   a0, s0
    call fs_find                    # the same name: overwrite it
    mv   s3, a0
    bgez s3, 2f
    li   s3, 0                      # otherwise the first free slot
1:  li   t0, FS_SLOTS
    li   a0, -1
    bge  s3, t0, 9f
    li   t0, FS_ENTRY
    mul  t0, s3, t0
    li   t1, DIR_BUFFER
    add  t1, t1, t0
    lw   t2, 28(t1)
    andi t2, t2, 1
    beqz t2, 2f
    addi s3, s3, 1
    j    1b
2:  # write the data
    addi a2, s2, 511
    srli a2, a2, 9
    beqz a2, 3f
    li   t0, FS_SLOT_SECTORS
    mul  a0, s3, t0
    addi a0, a0, FS_DATA_START
    mv   a1, s1
    call disk_write
3:  # then the directory entry
    li   t0, FS_ENTRY
    mul  t0, s3, t0
    li   t1, DIR_BUFFER
    add  s3, t1, t0                 # s3 = the entry
    mv   a0, s3
    li   a1, 0
    li   a2, FS_ENTRY
    call memset
    mv   a0, s3
    mv   a1, s0
    call strcpy
    sw   s2, 24(s3)
    li   t0, 1
    sw   t0, 28(s3)
    call fs_save_dir
    li   a0, 0
    j    9f
8:  li   a0, -3
9:  lw   s3, 12(sp)
    lw   s2, 16(sp)
    lw   s1, 20(sp)
    lw   s0, 24(sp)
    lw   ra, 28(sp)
    addi sp, sp, 32
    ret

# fs_delete(a0 = name) -> a0 = 0 OK, -1 no such file
fs_delete:
    addi sp, sp, -16
    sw   ra, 12(sp)
    call fs_find
    bltz a0, 1f
    li   t0, FS_ENTRY
    mul  t0, a0, t0
    li   t1, DIR_BUFFER
    add  a0, t1, t0
    li   a1, 0
    li   a2, FS_ENTRY
    call memset
    call fs_save_dir
    li   a0, 0
1:  lw   ra, 12(sp)
    addi sp, sp, 16
    ret

.data
.align 2
fs_ok:          .word 0
msg_nodisk:     .asciz "No disk: ls, cat, write and rm won't work.\n"
msg_formatting: .asciz "This disk is new: formatting it for LabFS... "
msg_formatted:  .asciz "done.\n"
msg_bytes:      .asciz " bytes\n"
.text
