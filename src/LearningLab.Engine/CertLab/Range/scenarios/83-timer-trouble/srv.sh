useradd -m -c '{{DEV_FULL}}' {{DEV}}
echo '{{DEV}}:{{PW}}' | chpasswd
useradd -m -c '{{OTHER_FULL}}' {{OTHER}}
mkdir -p /srv/designs
echo 'Spring catalogue - draft' > /srv/designs/catalogue.txt
echo 'New logo ideas' > /srv/designs/logo.txt
# the sign-in log: an ordinary colleague, then a few guesses and a success for the developer's account,
# then that account using sudo to write a new sudoers file
: > /var/log/secure
cat >> /var/log/secure <<LOG
Apr  3 08:55:10 srv sshd[2201]: Accepted password for {{OTHER}} from 10.10.23.40 port 50110 ssh2
Apr  3 23:40:02 srv sshd[3310]: Failed password for {{DEV}} from {{ATKIP}} port 41002 ssh2
Apr  3 23:40:09 srv sshd[3312]: Failed password for {{DEV}} from {{ATKIP}} port 41010 ssh2
Apr  3 23:40:17 srv sshd[3315]: Failed password for {{DEV}} from {{ATKIP}} port 41019 ssh2
Apr  3 23:40:26 srv sshd[3318]: Accepted password for {{DEV}} from {{ATKIP}} port 41027 ssh2
Apr  3 23:42:51 srv sudo[3350]:   {{DEV}} : TTY=pts/1 ; PWD=/home/{{DEV}} ; USER=root ; COMMAND=/usr/bin/tee /etc/sudoers.d/{{SUDOF}}
Apr  3 23:44:05 srv sudo[3361]:   {{DEV}} : TTY=pts/1 ; PWD=/home/{{DEV}} ; USER=root ; COMMAND=/usr/bin/systemctl enable --now {{TIMER}}.timer
Apr  4 08:31:44 srv sshd[4102]: Accepted password for {{OTHER}} from 10.10.23.40 port 50544 ssh2
LOG
# the new sudo rule: the developer account may now do anything as root, with no password
echo '{{DEV}} ALL=(ALL) NOPASSWD: ALL' > /etc/sudoers.d/{{SUDOF}}
chmod 440 /etc/sudoers.d/{{SUDOF}}
# the timer and the job it starts
mkdir -p /usr/local/lib/.{{TIMER}}
cat > /usr/local/lib/.{{TIMER}}/run.sh <<'SH'
#!/bin/bash
# bundle the design files for pickup
mkdir -p /var/tmp/.pickup
cp -r /srv/designs/* /var/tmp/.pickup/ 2>/dev/null
SH
chmod 755 /usr/local/lib/.{{TIMER}}/run.sh
cat > /etc/systemd/system/{{TIMER}}.service <<UNIT
[Unit]
Description={{TIMER_DESC}}

[Service]
Type=oneshot
ExecStart=/usr/local/lib/.{{TIMER}}/run.sh
UNIT
cat > /etc/systemd/system/{{TIMER}}.timer <<UNIT
[Unit]
Description={{TIMER_DESC}} (every 15 minutes)

[Timer]
OnCalendar=*:0/15
Persistent=true

[Install]
WantedBy=timers.target
UNIT
systemctl daemon-reload
systemctl enable --now {{TIMER}}.timer
