useradd -m -c 'Pilot account' pilot
echo 'pilot:{{SPW}}' | chpasswd
# the same password the handover note mentions is reused on the inside server
echo 'pilot:{{JPW}}' | chpasswd
echo 'This is the inside storage server. Not reachable from outside.' > /etc/motd
