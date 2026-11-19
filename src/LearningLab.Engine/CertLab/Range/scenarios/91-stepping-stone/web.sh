useradd -m -c 'IT admin' itadmin
echo 'itadmin:{{ITPW}}' | chpasswd
usermod -aG wheel itadmin
systemctl enable --now httpd
mkdir -p /var/www/html/uploads
chown apache: /var/www/html/uploads
echo '<h1>{{CO}} orders</h1>' > /var/www/html/index.html
cat > /var/www/html/config.php <<CONF
<?php
// order database on the Windows server
\$db_host = '10.10.31.40';
\$db_user = 'svc_web';
\$db_pass = '{{WEBPW}}';
CONF
chmod 644 /var/www/html/config.php
mkdir -p /var/log/httpd
cat > /var/log/httpd/access_log <<LOG
198.51.100.31 - - [01/Mar/2026:10:02:11 +0000] "GET / HTTP/1.1" 200 640 "-" "Mozilla/5.0"
203.0.113.12 - - [01/Mar/2026:11:20:40 +0000] "GET /order.php?id=88 HTTP/1.1" 200 1203 "-" "Mozilla/5.0"
{{ATKIP}} - - [01/Mar/2026:23:10:05 +0000] "POST /upload.php HTTP/1.1" 200 44 "-" "curl/8.2.1"
{{ATKIP}} - - [01/Mar/2026:23:10:31 +0000] "GET /uploads/{{SHELL}}?{{P}}=id HTTP/1.1" 200 51 "-" "curl/8.2.1"
{{ATKIP}} - - [01/Mar/2026:23:11:02 +0000] "GET /uploads/{{SHELL}}?{{P}}=cat+../config.php HTTP/1.1" 200 160 "-" "curl/8.2.1"
198.51.100.31 - - [02/Mar/2026:08:15:12 +0000] "GET / HTTP/1.1" 200 640 "-" "Mozilla/5.0"
LOG
