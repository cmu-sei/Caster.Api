/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

using System;

namespace Caster.Api.Features.Hosts
{
    public class Host
    {
        /// <summary>
        /// ID of the Host.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the Host.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The name of the datastore to use for this Host
        /// </summary>
        public string Datastore { get; set; }

        /// <summary>
        /// The maximum number of machines to deploy to this Host for DynamicHost Workspaces
        /// </summary>
        public int MaximumMachines { get; set; }

        /// <summary>
        /// If true, use this Host when deploying to a DynamicHost Workspace
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// If true, use this Host only for manual deployments
        /// </summary>
        public bool Development { get; set; }

        /// <summary>
        /// The Project this Host is assigned to, if any
        /// </summary>
        public Guid? ProjectId { get; set; }
    }
}
