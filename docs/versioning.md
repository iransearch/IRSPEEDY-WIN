# Windows app versioning

Starting with 1.4.7, public releases use three components: Major.Minor.Patch.
The current patch release is 1.4.60; do not resume the previous fourth-component counter.
Keep AssemblyVersion, AssemblyFileVersion and AssemblyInformationalVersion in
IRSpeedyVPN/Properties/AssemblyInfo.cs aligned. The app header and settings API
request use three components. The CLR's numeric assembly version still carries
an implicit zero revision, which does not appear in the public version string.




