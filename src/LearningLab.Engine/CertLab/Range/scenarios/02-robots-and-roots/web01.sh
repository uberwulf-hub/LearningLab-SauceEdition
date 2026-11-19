useradd -m -c 'Deploy account' deploy
echo 'deploy:{{PW1}}' | chpasswd
systemctl enable --now httpd
cat > /var/www/html/index.html <<'HTML'
<html><body><h1>Larkspur Studios</h1><p>Coming soon.</p></body></html>
HTML
cat > /var/www/html/robots.txt <<'TXT'
User-agent: *
Disallow: /site-backup/
TXT
mkdir -p /var/www/html/site-backup
cat > /var/www/html/site-backup/deploy-notes.txt <<TXT
Deployment notes (do not publish)
---------------------------------
Server: web01
SSH account: deploy
Password: {{PW1}}

Remember to remove this folder before go-live.
TXT
echo 'deploy ALL=(root) NOPASSWD: /usr/bin/awk' > /etc/sudoers.d/deploy
chmod 440 /etc/sudoers.d/deploy
