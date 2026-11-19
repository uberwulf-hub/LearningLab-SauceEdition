useradd -m -c 'Web administrator' webadmin
# a login shell with a key but no password set
usermod -s /bin/bash webadmin
systemctl enable --now httpd
cat > /var/www/html/index.html <<'HTML'
<html><body><h1>Marigold Media</h1></body></html>
HTML
mkdir -p /var/www/html/backup
# make a real key pair for webadmin and publish the private half by mistake
su - webadmin -c 'mkdir -p ~/.ssh && chmod 700 ~/.ssh && ssh-keygen -t ed25519 -N "" -f ~/.ssh/id_ed25519 -q && cat ~/.ssh/id_ed25519.pub >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys'
cp /home/webadmin/.ssh/id_ed25519 /var/www/html/backup/id_rsa
chmod 644 /var/www/html/backup/id_rsa
echo 'Old site backup. TODO: delete before launch.' > /var/www/html/backup/README.txt
# root runs a world-writable maintenance script every minute
mkdir -p /opt/maint
cat > /opt/maint/rotate.sh <<'SH'
#!/bin/bash
# rotate temp logs
: > /var/log/app-temp.log 2>/dev/null
SH
chmod 777 /opt/maint/rotate.sh
echo '* * * * * root /opt/maint/rotate.sh' > /etc/cron.d/maint
