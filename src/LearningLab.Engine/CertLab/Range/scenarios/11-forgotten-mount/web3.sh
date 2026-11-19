useradd -m {{SVCUSER}}
echo '{{SVCUSER}}:{{DBPW}}' | chpasswd
systemctl enable --now httpd
echo '<h1>{{CO}}</h1><p>Coming soon.</p>' > /var/www/html/index.html
mkdir -p /var/www/html/backup
mkdir -p /tmp/site/config
cat > /tmp/site/config/db.php <<'PHP'
<?php
// database settings - do not publish
$db_host = 'localhost';
$db_name = 'portal';
$db_user = '{{SVCUSER}}';
$db_pass = '{{DBPW}}';
PHP
tar czf /var/www/html/backup/site-backup.tar.gz -C /tmp site
rm -rf /tmp/site
chmod 644 /var/www/html/backup/site-backup.tar.gz
echo 'Old site backup - TODO remove before launch.' > /var/www/html/backup/README.txt
echo '{{SVCUSER}} ALL=(root) NOPASSWD: {{SUDOBIN}}' > /etc/sudoers.d/{{SVCUSER}}
chmod 440 /etc/sudoers.d/{{SVCUSER}}
