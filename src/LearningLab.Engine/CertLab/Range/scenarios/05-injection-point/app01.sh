useradd -m -c 'Application service account' appsvc
echo 'appsvc:{{SVCPW}}' | chpasswd
systemctl enable --now httpd
mkdir -p /var/www/conf
cat > /var/www/conf/app.ini <<CFG
[app]
name = NetCheck Lite
[service]
user = appsvc
password = {{SVCPW}}
CFG
chmod 644 /var/www/conf/app.ini
echo 'appsvc ALL=(root) NOPASSWD: /usr/bin/sed' > /etc/sudoers.d/appsvc
chmod 440 /etc/sudoers.d/appsvc
