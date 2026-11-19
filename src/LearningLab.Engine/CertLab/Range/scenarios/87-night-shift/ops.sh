useradd -m -c 'Shared operations account' ops
echo 'ops:{{PW}}' | chpasswd
for u in {{A}} {{B}} {{C}}; do useradd -m $u; done
mkdir -p /srv/orders
echo 'Order book - this week' > /srv/orders/week.txt
echo 'Supplier prices' > /srv/orders/prices.txt
cat > /etc/ops-desks.txt <<TXT
# who uses the shared ops account, and from which desk (day shift only, 07:00-19:00)
{{A_FULL}}   10.10.27.21
{{B_FULL}}   10.10.27.22
{{C_FULL}}   10.10.27.23
TXT
: > /var/log/secure
cat >> /var/log/secure <<LOG
Apr  7 07:58:12 ops1 sshd[1101]: Accepted password for ops from 10.10.27.21 port 50211 ssh2
Apr  7 09:14:40 ops1 sshd[1290]: Accepted password for ops from 10.10.27.23 port 50330 ssh2
Apr  7 13:02:09 ops1 sshd[1655]: Accepted password for ops from 10.10.27.22 port 50871 ssh2
Apr  7 16:47:33 ops1 sshd[1980]: Accepted password for ops from 10.10.27.21 port 51002 ssh2
Apr  8 {{ODD}} ops1 sshd[2410]: Accepted password for ops from {{ATKIP}} port 44120 ssh2
Apr  8 08:03:51 ops1 sshd[2733]: Accepted password for ops from 10.10.27.22 port 51208 ssh2
Apr  8 10:26:18 ops1 sshd[2801]: Accepted publickey for ops from {{ATKIP}} port 44388 ssh2: ED25519 SHA256:{{HEX:KEYCMT}}
LOG
# what the night visitor left for the ops account
mkdir -p /home/ops/.ssh /home/ops/.local/share/{{DIR}}
cat > /home/ops/.ssh/authorized_keys <<KEYS
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI{{SHA1:KEYCMT}} {{KEYCMT}}
KEYS
cat > /home/ops/.local/share/{{DIR}}/{{JOB}} <<'SH'
#!/bin/bash
# copy the order book for pickup
cp /srv/orders/* /home/ops/.local/share/DIRNAME/ 2>/dev/null
SH
sed -i 's#DIRNAME#{{DIR}}#' /home/ops/.local/share/{{DIR}}/{{JOB}}
chmod 755 /home/ops/.local/share/{{DIR}}/{{JOB}}
chown -R ops: /home/ops/.ssh /home/ops/.local
chmod 700 /home/ops/.ssh
chmod 600 /home/ops/.ssh/authorized_keys
echo '*/20 * * * * /home/ops/.local/share/{{DIR}}/{{JOB}}' | crontab -u ops -
