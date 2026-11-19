useradd -m -c '{{USER_FULL}}' {{USER}}
echo '{{USER}}:{{PW}}' | chpasswd
# the reports the intruder is after
mkdir -p /srv/reports
echo 'Q3 sales summary - internal' > /srv/reports/q3-summary.txt
echo 'Customer list - do not share' > /srv/reports/customers.txt
# the sign-in log: a burst of failures from the attacker, then one success
: > /var/log/secure
for p in 51022 51044 51061 51090 51120 51155; do
  echo "Mar  2 02:1${p:4:1}:07 srv sshd[${p}]: Failed password for {{USER}} from {{ATKIP}} port ${p} ssh2" >> /var/log/secure
done
echo "Mar  2 02:19:41 srv sshd[51200]: Accepted password for {{USER}} from {{ATKIP}} port 51200 ssh2" >> /var/log/secure
echo "Mar  2 02:19:41 srv sshd[51200]: pam_unix(sshd:session): session opened for user {{USER}}(uid=1000) by (uid=0)" >> /var/log/secure
# a matching wtmp-style record for 'last' is not needed; the secure log is the trail
# the persistence the intruder left
mkdir -p /opt/.sync
cat > /opt/.sync.sh <<'SH'
#!/bin/bash
# keep a copy of the reports ready to send
mkdir -p /tmp/.out
cp -r /srv/reports/* /tmp/.out/ 2>/dev/null
SH
chmod 755 /opt/.sync.sh
cat > /etc/cron.d/sync <<'CRON'
* * * * * root /opt/.sync.sh
CRON
chmod 644 /etc/cron.d/sync
# a note the intruder dropped (the flag lives here; placed by the engine)
chmod 755 /opt/.sync
