#!/bin/bash
scp state/dashboard/* oci:/tmp/ && ssh oci 'sudo cp /tmp/* /opt/dashboard/ && sudo chown -R www-data:www-data /opt/dashboard/ && rm /tmp/*'
