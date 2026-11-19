systemctl enable --now httpd
mkdir -p /var/www/html/uploads
chown apache:apache /var/www/html/uploads
# the public access log: normal traffic, then the attacker uploads and uses a web shell
mkdir -p /var/log/httpd
cat > /var/log/httpd/access_log <<LOG
203.0.113.9 - - [04/Mar/2026:09:01:11 +0000] "GET / HTTP/1.1" 200 1532 "-" "Mozilla/5.0"
198.51.100.7 - - [04/Mar/2026:09:03:52 +0000] "GET /about.html HTTP/1.1" 200 884 "-" "Mozilla/5.0"
{{ATKIP}} - - [04/Mar/2026:23:41:03 +0000] "POST /upload.php HTTP/1.1" 200 51 "-" "curl/8.2.1"
{{ATKIP}} - - [04/Mar/2026:23:41:20 +0000] "GET /uploads/{{SHELL}}.php?cmd=id HTTP/1.1" 200 44 "-" "curl/8.2.1"
{{ATKIP}} - - [04/Mar/2026:23:42:05 +0000] "GET /uploads/{{SHELL}}.php?cmd=whoami HTTP/1.1" 200 8 "-" "curl/8.2.1"
{{ATKIP}} - - [04/Mar/2026:23:45:38 +0000] "GET /uploads/{{SHELL}}.php?cmd=uname+-a HTTP/1.1" 200 120 "-" "curl/8.2.1"
LOG
# the rogue service the attacker left (kept running so it can beacon out)
cat > /etc/systemd/system/{{SVC}}.service <<UNIT
[Unit]
Description=System telemetry helper

[Service]
Type=simple
ExecStart=/bin/sleep infinity

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable --now {{SVC}}
# the web shell file is placed by the engine (it carries the flag)
