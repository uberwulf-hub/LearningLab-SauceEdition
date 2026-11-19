useradd -m -c 'Dana Ruiz' dana
echo 'dana:{{PW1}}' | chpasswd
useradd -m -c 'Office scanner account' scanner
cat > /var/ftp/pub/welcome.txt <<'TXT'
Welcome to the Northwind Books file server!

Scanned invoices go in /srv/invoices. Ask Dana if you need access.
TXT
cat > /var/ftp/pub/new-staff.txt <<'TXT'
Hi Dana,

Your account on files01 is ready. User name: dana
Temporary password: {{PW1}}

Please change it the first time you sign in (passwd).
- IT
TXT
sed -i 's/^anonymous_enable=NO/anonymous_enable=YES/' /etc/vsftpd/vsftpd.conf
systemctl enable --now vsftpd
echo 'dana ALL=(root) NOPASSWD: /usr/bin/find' > /etc/sudoers.d/dana
chmod 440 /etc/sudoers.d/dana
mkdir -p /srv/invoices
echo 'Invoice 1042: 12 boxes of bookmarks' > /srv/invoices/1042.txt
