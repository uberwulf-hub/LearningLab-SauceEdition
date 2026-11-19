useradd -m -c 'Pilot account' pilot
echo 'pilot:{{JPW}}' | chpasswd
sed -i 's/^anonymous_enable=NO/anonymous_enable=YES/' /etc/vsftpd/vsftpd.conf
systemctl enable --now vsftpd
cat > /var/ftp/pub/handover.txt <<TXT
Shift handover notes
--------------------
The jump host (jump01) reaches the inside network for us.
Login here and on the inside boxes: pilot / {{JPW}}
TXT
