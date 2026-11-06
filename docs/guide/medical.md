# Medical data

`Tachyon.Charts.Medical` is an optional add-on for displaying and analysing image volumes with the 2D and 3D charts. Install it separately:

```bash
dotnet add package Tachyon.Charts.Medical
```

The add-on is intended as a technical building block for research, education and analytical applications. It does not provide clinical workflows, diagnoses or treatment recommendations.

> **Not a medical device:** Tachyon Charts and its medical add-on are not medical devices and are not approved or certified for diagnosis or therapy under the EU Medical Device Regulation (MDR). The application developer and operator must assess the intended use, validate the complete application and meet every regulatory requirement that applies to it.

## Privacy and data minimisation

Medical images can contain health data, which is special-category personal data under Article 9 of the General Data Protection Regulation (GDPR). An application using the add-on remains responsible for its lawful basis, access control, retention periods, encryption, audit logging, exports and every other organisational or technical safeguard.

The built-in DICOM reader follows these limits:

- It reads only pixel data and tags needed to interpret image geometry and values.
- It may use the modality and Series Instance UID to interpret or group images.
- It skips patient names, dates of birth, patient and study identifiers and every other unrelated tag.
- Skipped values are never exposed by the API, retained in memory, written to a log or included in an export.

These limits apply data minimisation under GDPR Article 5 and privacy by design and by default under Article 25. They do not make a file anonymous: text and identifiers can be burned into the pixels themselves. Applications that accept external files must inspect and handle pixel data according to their own privacy policy.

The reader deliberately has no switch for loading patient information. If an application has a lawful reason to process additional tags, it must do so through a separate component under its own responsibility.

## Package boundary

The medical package builds on `Tachyon.Charts.ThreeD`, which in turn builds on the platform-neutral chart core. The regular 2D and 3D packages do not reference the medical package, so applications that do not install it receive no medical-data API.
