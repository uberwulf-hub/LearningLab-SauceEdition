useradd -m -c 'IT admin' itadmin
echo 'itadmin:{{ITPW}}' | chpasswd
usermod -aG wheel itadmin
useradd -m -c '{{USER_FULL}}' {{USER}}
echo '{{USER}}:{{PW}}' | chpasswd
mkdir -p /srv/hr
echo 'Staff reviews 2026' > /srv/hr/reviews.txt
# the sign-in: written through the system logger, so the journal has it too
logger -p authpriv.info -t sshd "Accepted password for {{USER}} from {{ATKIP}} port 52114 ssh2"
logger -p authpriv.info -t sshd "pam_unix(sshd:session): session opened for user {{USER}}"
# ...and then the intruder cut those lines out of /var/log/secure (the journal keeps its own copy)
grep -v '{{ATKIP}}' /var/log/secure | grep -v 'session opened for user {{USER}}' > /var/log/secure.new
cat /var/log/secure.new > /var/log/secure
rm -f /var/log/secure.new
# what they left in the account
mkdir -p /home/{{USER}}/.config/{{HIDE}}
cat > /home/{{USER}}/.config/{{HIDE}}/pull.sh <<'SH'
#!/bin/bash
# copies the HR folder for pickup
cp /srv/hr/* "$(dirname "$0")/" 2>/dev/null
SH
chmod 755 /home/{{USER}}/.config/{{HIDE}}/pull.sh
chown -R {{USER}}: /home/{{USER}}/.config
echo '*/15 * * * * /home/{{USER}}/.config/{{HIDE}}/pull.sh' | crontab -u {{USER}} -
