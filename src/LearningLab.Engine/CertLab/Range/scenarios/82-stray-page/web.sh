systemctl enable --now httpd
echo '<h1>{{CO}}</h1><p>Welcome! Send us your photos with the upload form.</p>' > /var/www/html/index.html
echo '<form method="post" enctype="multipart/form-data"><input type="file" name="f"></form>' > /var/www/html/upload.php
mkdir -p /var/www/html/uploads
# the photos visitors sent
echo 'JPEG photo (pretend)' > /var/www/html/uploads/{{PHOTO1}}.jpg
echo 'JPEG photo (pretend)' > /var/www/html/uploads/{{PHOTO2}}.jpg
# someone left the folder writable by every account on the server
chmod 777 /var/www/html/uploads
mkdir -p /var/log/httpd
cat > /var/log/httpd/access_log <<LOG
198.51.100.23 - - [11/Apr/2026:09:12:40 +0000] "GET / HTTP/1.1" 200 812 "-" "Mozilla/5.0"
198.51.100.23 - - [11/Apr/2026:09:13:02 +0000] "POST /upload.php HTTP/1.1" 200 44 "-" "Mozilla/5.0"
203.0.113.77 - - [11/Apr/2026:10:40:19 +0000] "GET /uploads/{{PHOTO1}}.jpg HTTP/1.1" 200 20311 "-" "Mozilla/5.0"
{{ATKIP}} - - [11/Apr/2026:22:51:07 +0000] "GET /upload.php HTTP/1.1" 200 120 "-" "curl/8.2.1"
{{ATKIP}} - - [11/Apr/2026:22:51:30 +0000] "POST /upload.php HTTP/1.1" 200 44 "-" "curl/8.2.1"
{{ATKIP}} - - [11/Apr/2026:22:52:11 +0000] "GET /uploads/{{FILE}} HTTP/1.1" 200 310 "-" "curl/8.2.1"
{{ATKIP}} - - [11/Apr/2026:22:58:45 +0000] "GET /uploads/{{FILE}}{{QUERY}} HTTP/1.1" 200 62 "-" "curl/8.2.1"
203.0.113.77 - - [12/Apr/2026:08:02:55 +0000] "GET / HTTP/1.1" 200 812 "-" "Mozilla/5.0"
LOG
# the dropped page itself is placed by the engine (it carries the flag)
