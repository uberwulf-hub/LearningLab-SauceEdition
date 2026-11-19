useradd -m -c '{{USER_FULL}}' {{USER}}
echo '{{USER}}:{{PW1}}' | chpasswd
cat > /var/ftp/pub/welcome.txt <<'TXT'
Welcome to the {{CO}} file drop. Public files only, please.
TXT
cat > /var/ftp/pub/new-staff.txt <<TXT
Hi {{USER_FIRST}},

Your account on {{HOST}} is ready.
  user: {{USER}}
  temporary password: {{PW1}}

Please change it the first time you sign in.
Note: every sudo is logged and reviewed by IT.
TXT
sed -i 's/^anonymous_enable=NO/anonymous_enable=YES/' /etc/vsftpd/vsftpd.conf
systemctl enable --now vsftpd
# the bait: a sudo rule that works - and is watched
echo '{{USER}} ALL=(root) NOPASSWD: /usr/bin/find' > /etc/sudoers.d/{{USER}}
chmod 440 /etc/sudoers.d/{{USER}}
# the careless mistake: a set-user-ID copy of a text tool, owned by root
install -m0755 {{SRC}} /usr/local/bin/{{TOOL}}
chown root: /usr/local/bin/{{TOOL}}
chmod u+s /usr/local/bin/{{TOOL}}
