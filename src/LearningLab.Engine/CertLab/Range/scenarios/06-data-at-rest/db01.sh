useradd -m -c 'Database administrator' dbadmin
echo 'dbadmin:{{ADMPW}}' | chpasswd
systemctl enable --now httpd
mkdir -p /var/www/docs /var/www/conf
echo 'Staff handbook: be kind, wash your hands.' > /var/www/docs/handbook.txt
cat > /var/www/conf/db.ini <<CFG
[database]
host = localhost
name = intranet
user = intranet
password = {{DBPW}}
CFG
chmod 644 /var/www/conf/db.ini
# the pretend database the mysql client reads
mkdir -p /var/lib/mysql/intranet
echo 'intranet:{{DBPW}}:intranet' > /var/lib/mysql/range-users.txt
printf 'id\tname\trole\tlinux_user\tlinux_password\n' > /var/lib/mysql/intranet/staff.tsv
printf '1\tReya\tnurse\t-\t-\n' >> /var/lib/mysql/intranet/staff.tsv
printf '2\tOmar\tadmin\tdbadmin\t{{ADMPW}}\n' >> /var/lib/mysql/intranet/staff.tsv
printf '3\tLin\treception\t-\t-\n' >> /var/lib/mysql/intranet/staff.tsv
systemctl enable --now mariadb
echo 'dbadmin ALL=(root) NOPASSWD: /usr/bin/awk' > /etc/sudoers.d/dbadmin
chmod 440 /etc/sudoers.d/dbadmin
