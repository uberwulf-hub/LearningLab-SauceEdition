useradd -m -s /bin/bash -c 'Operator account' renter
echo 'renter:{{OPPW}}' | chpasswd
printf '%s\n' 'Birchwood application server.' > /etc/motd
# a set-user-ID copy of a text tool, owned by root (the careless mistake)
install -m0755 /usr/bin/cat /usr/local/bin/readlog
chown root: /usr/local/bin/readlog
chmod u+s /usr/local/bin/readlog
