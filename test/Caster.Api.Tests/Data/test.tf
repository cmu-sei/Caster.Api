module "person-course-machines" {
  source            = "git::https://gitlab.local/terraform-modules/person-course-machines.git?ref=master"
  project_id       = "${var.project_id}"
  admin_team_id     = "${var.admin}"
  user_team_id      = "${var.student}"
  user_id           = "${var.user_id}"
  username          = "${var.username}"
  vsphere_host_name = "${var.vsphere_host_name}"
}

module "person-course-machines" {
  source            = "git::https://gitlab.local/terraform-modules/person-course-machines.git?ref=master"
  project_id       = "${var.project_id}"
  admin_team_id     = "${var.admin}"
  user_team_id      = "${var.student}"
  user_id           = "${var.user_id}"
  username          = "${var.username}"
  vsphere_host_name = "${var.vsphere_host_name}"
}
