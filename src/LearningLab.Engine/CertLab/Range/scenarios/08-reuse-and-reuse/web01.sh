systemctl enable --now httpd
# a few accounts; renter's password is the one that is reused elsewhere
useradd -M decoy1 2>/dev/null; echo 'decoy1:Zx9$kLmQ7pR!2w' | chpasswd
useradd -M decoy2 2>/dev/null; echo 'decoy2:Qw8&nBvC3xZ@1m' | chpasswd
useradd -M renter 2>/dev/null; echo 'renter:{{OPPW}}' | chpasswd
mkdir -p /var/www/html/backups
{ grep '^decoy1:' /etc/shadow; grep '^decoy2:' /etc/shadow; grep '^renter:' /etc/shadow; } | cut -d: -f1,2 > /var/www/html/backups/accounts.bak
chmod 644 /var/www/html/backups/accounts.bak
echo 'Old account export. Delete me.' > /var/www/html/backups/README.txt
cat > /var/www/html/index.html <<'HTML'
<html><body><h1>Birchwood Rentals</h1></body></html>
HTML
