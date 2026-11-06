# Tachyon Charts Medical

This optional add-on provides privacy-first medical image and volume support for Tachyon Charts.

> **Not a medical device:** This library is not approved or certified for diagnosis or therapy. Application developers and operators are responsible for intended use, validation, privacy and every applicable regulatory requirement.

The DICOM reader deliberately exposes only pixels, image geometry and value interpretation. It does not expose patient information. The user guide describes the package boundary, data-minimisation rules and regulatory scope.

Supported input is uncompressed DICOM Part 10 with Implicit VR Little Endian, Explicit VR Little Endian or Explicit VR Big Endian transfer syntax and signed or unsigned 8- or 16-bit grayscale pixels. `DicomSeriesReader` sorts single-frame slices by patient-space position and produces a regularly spaced `VolumeData`.
