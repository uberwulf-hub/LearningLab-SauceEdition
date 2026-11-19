useradd -m -c '{{USER_FULL}}' {{USER}}
echo '{{USER}}:{{PW1}}' | chpasswd
cat > /var/ftp/pub/welcome.txt <<'TXT'
Welcome to the shop file drop. Scanned receipts go in /srv/receipts.
TXT
cat > /var/ftp/pub/new-staff.txt <<TXT
Hi {{USER_FIRST}},

Your account on store1 is ready.
  user: {{USER}}
  temporary password: {{PW1}}

Please change it the first time you sign in.
- IT
TXT
sed -i 's/^anonymous_enable=NO/anonymous_enable=YES/' /etc/vsftpd/vsftpd.conf
systemctl enable --now vsftpd
echo '{{USER}} ALL=(root) NOPASSWD: {{SUDOBIN}}' > /etc/sudoers.d/{{USER}}
chmod 440 /etc/sudoers.d/{{USER}}
mkdir -p /srv/receipts
echo 'Receipt 1042: 3 boxes' > /srv/receipts/1042.txt
