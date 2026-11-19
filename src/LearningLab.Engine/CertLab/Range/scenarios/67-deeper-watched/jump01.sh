useradd -m -c 'Pilot account' pilot
echo 'pilot:{{JPW}}' | chpasswd
sed -i 's/^anonymous_enable=NO/anonymous_enable=YES/' /etc/vsftpd/vsftpd.conf
systemctl enable --now vsftpd
cat > /var/ftp/pub/handover.txt <<TXT
Shift handover notes
--------------------
The jump host (jump01) reaches the inside network for us.
Login here and on the inside storage box: pilot / {{JPW}}
Reminder: the inside network has a sensor now - no scanning in there!
TXT
{{if LOOK=hosts}}
echo '10.10.72.{{INNER}} store01 store01.thornfield.example' >> /etc/hosts
{{end}}
{{if LOOK=history}}
cat > /home/pilot/.bash_history <<TXT
df -h
ssh pilot@10.10.72.{{INNER}}
uptime
TXT
chown pilot: /home/pilot/.bash_history
chmod 600 /home/pilot/.bash_history
{{end}}
{{if LOOK=sshconfig}}
mkdir -p /home/pilot/.ssh
cat > /home/pilot/.ssh/config <<TXT
Host store01
    HostName 10.10.72.{{INNER}}
    User pilot
TXT
chown -R pilot: /home/pilot/.ssh
chmod 700 /home/pilot/.ssh
chmod 600 /home/pilot/.ssh/config
{{end}}
