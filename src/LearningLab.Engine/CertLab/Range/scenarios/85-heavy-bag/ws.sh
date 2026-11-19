useradd -m -c 'IT admin' itadmin
echo 'itadmin:{{ITPW}}' | chpasswd
usermod -aG wheel itadmin
useradd -m -c '{{STAFF_FULL}}' {{STAFF}}
mkdir -p /srv/clients
echo 'Client list - private' > /srv/clients/clients.txt
echo 'Quotes for next year' > /srv/clients/quotes.txt
# the job that bundles the client files and posts them through the proxy
mkdir -p /var/tmp/{{STAGE}}
cat > /usr/local/bin/{{SCRIPT}} <<'SH'
#!/bin/bash
# "backup" - bundles the client folder and sends it off-site
tar -cf /var/tmp/STAGEDIR/batch.tar /srv/clients 2>/dev/null
SH
sed -i 's#STAGEDIR#{{STAGE}}#' /usr/local/bin/{{SCRIPT}}
chmod 755 /usr/local/bin/{{SCRIPT}}
cat > /etc/cron.d/{{SCRIPT}} <<CRON
30 2 * * * root /usr/local/bin/{{SCRIPT}}
CRON
chmod 644 /etc/cron.d/{{SCRIPT}}
chmod 755 /var/tmp/{{STAGE}}
