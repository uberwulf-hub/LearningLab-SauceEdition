useradd -m -c 'IT admin' itadmin
echo 'itadmin:{{ITPW}}' | chpasswd
usermod -aG wheel itadmin
useradd -m -c 'Scanner upload account' scanner
echo 'scanner:{{SCANPW}}' | chpasswd
: > /var/log/secure
cat >> /var/log/secure <<LOG
Mar  1 09:12:01 scan1 sshd[1201]: Accepted password for itadmin from 10.10.30.5 port 51002 ssh2
Mar  1 22:47:15 scan1 sshd[2210]: Failed password for scanner from {{ATKIP}} port 40021 ssh2
Mar  1 22:47:31 scan1 sshd[2214]: Accepted password for scanner from {{ATKIP}} port 40029 ssh2
LOG
# the scanner account's shell history: it read a saved password and reached the file server with it
cat > /home/scanner/.bash_history <<HIST
ls
cat /etc/scan-to-share.conf
winrm-shell -i 10.10.30.40 -u {{SVC}} -p {{SVCPW}}
exit
HIST
cat > /etc/scan-to-share.conf <<CONF
# the scanner drops scans on the file server's share with this account
share=//10.10.30.40/Team
user={{SVC}}
password={{SVCPW}}
CONF
chmod 644 /etc/scan-to-share.conf
chown scanner: /home/scanner/.bash_history
